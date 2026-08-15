using BepInEx;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine.Networking;

namespace Shadowbus.LLMAI
{
    internal sealed class LLMApiResult
    {
        public bool Success;
        public string Content;
        public string Error;
        public long HttpStatus;
        public string ApiMode;
        public string ResponseId;
        public OpenAIUsage Usage;
        public string FallbackReason;
    }

    internal static class LLMOpenAIClient
    {
        private const string DefaultSystemPrompt =
            "You are the opponent AI in Shadowverse. Return exactly one JSON object and no markdown. " +
            "Choose only legal actions and preserve target order and exact card refs. Decision order is: " +
            "verified lethal, avoid your own defeat, gain information, board control and tempo, preserve resources, end turn. " +
            "If drawing or searching this turn, normally do it before irreversible PP spending unless an earlier action " +
            "reduces cost, refunds PP, frees hand space, or is part of verified lethal. " +
            "Actions marked requires_replan_after must end the plan and set replan_after=true. " +
            "Only use visible rule counters when visible card text refers to the corresponding mechanic.";

        internal static IEnumerator RequestPlan(
            LLMAISettings settings,
            BattleSnapshotDto snapshot,
            Action<UnityWebRequest> onRequestCreated,
            Action<LLMApiResult> onComplete)
        {
            LLMEndpointSet endpoints = LLMEndpointResolver.Resolve(settings);
            if (settings.ApiMode == LLMApiMode.ChatCompletions)
            {
                return RequestChat(settings, snapshot, endpoints.ChatCompletions, onRequestCreated, onComplete, null, true);
            }
            return RequestResponses(settings, snapshot, endpoints, onRequestCreated, onComplete,
                true, settings.ApiMode == LLMApiMode.Auto, null);
        }

        private static IEnumerator RequestResponses(
            LLMAISettings settings,
            BattleSnapshotDto snapshot,
            LLMEndpointSet endpoints,
            Action<UnityWebRequest> onRequestCreated,
            Action<LLMApiResult> onComplete,
            bool useTextFormat,
            bool allowChatFallback,
            string fallbackReason)
        {
            string snapshotJson = JsonConvert.SerializeObject(snapshot, Formatting.None);
            OpenAIResponsesRequest payload = new OpenAIResponsesRequest
            {
                Model = settings.Model,
                Instructions = BuildPrompt(settings.PromptFile),
                Input = "Return one TurnPlan JSON object.\n" + snapshotJson,
                MaxOutputTokens = settings.MaxOutputTokens,
                Reasoning = CreateReasoning(settings.ReasoningEffort),
                Store = false,
                Text = useTextFormat
                    ? new OpenAITextConfiguration { Format = OpenAIResponsesProtocol.CreateTurnPlanFormat() }
                    : null
            };
            if (settings.DebugLogPayloads)
            {
                LLMDecisionLog.Debug("PAYLOAD", "responses request=" + snapshotJson);
            }

            LLMHttpResult http = null;
            yield return Send(endpoints.Responses, JsonConvert.SerializeObject(payload, Formatting.None), settings,
                onRequestCreated, result => http = result);
            if (!http.Success)
            {
                if (useTextFormat && LLMApiFallbackPolicy.IsResponsesTextFormatUnsupported(http.Status, http.Error))
                {
                    LLMDecisionLog.Warning(
                        "API",
                        $"Responses HTTP {http.Status} rejected text.format; retrying once with prompt-constrained JSON");
                    yield return RequestResponses(settings, snapshot, endpoints, onRequestCreated, onComplete,
                        false, allowChatFallback, "responses_text_format_unsupported");
                    yield break;
                }
                if (allowChatFallback && LLMApiFallbackPolicy.ShouldFallbackToChat(http.Status, http.Error, http.TimedOut))
                {
                    string reason = "responses_endpoint_incompatible:" + Safe(http.Error);
                    LLMDecisionLog.Warning(
                        "API",
                        $"Responses HTTP {http.Status} is incompatible; falling back once to Chat Completions: " +
                        Safe(http.Error));
                    yield return RequestChat(settings, snapshot, endpoints.ChatCompletions, onRequestCreated,
                        onComplete, reason, true);
                    yield break;
                }
                onComplete(Failure(http, "responses", fallbackReason));
                yield break;
            }

            if (!OpenAIResponsesProtocol.TryExtract(http.Body, out string content, out string responseId,
                    out OpenAIUsage usage, out string parseError))
            {
                onComplete(new LLMApiResult
                {
                    Success = false,
                    Error = parseError,
                    HttpStatus = http.Status,
                    ApiMode = "responses",
                    ResponseId = responseId,
                    Usage = usage,
                    FallbackReason = fallbackReason
                });
                yield break;
            }
            if (settings.DebugLogPayloads)
            {
                LLMDecisionLog.Debug("PAYLOAD", "responses response=" + content);
            }
            onComplete(new LLMApiResult
            {
                Success = true,
                Content = content,
                HttpStatus = http.Status,
                ApiMode = "responses",
                ResponseId = responseId,
                Usage = usage,
                FallbackReason = fallbackReason
            });
        }

        private static IEnumerator RequestChat(
            LLMAISettings settings,
            BattleSnapshotDto snapshot,
            string endpoint,
            Action<UnityWebRequest> onRequestCreated,
            Action<LLMApiResult> onComplete,
            string fallbackReason,
            bool useJsonResponseFormat)
        {
            string snapshotJson = JsonConvert.SerializeObject(snapshot, Formatting.None);
            OpenAIChatRequest payload = new OpenAIChatRequest
            {
                Model = settings.Model,
                MaxTokens = settings.MaxResponseTokens,
                ReasoningEffort = NormalizeEffort(settings.ReasoningEffort),
                ResponseFormat = useJsonResponseFormat ? new { type = "json_object" } : null,
                Messages = new List<OpenAIChatMessage>
                {
                    new OpenAIChatMessage { Role = "system", Content = BuildPrompt(settings.PromptFile) },
                    new OpenAIChatMessage { Role = "user", Content = "Return one TurnPlan JSON object.\n" + snapshotJson }
                }
            };
            if (settings.DebugLogPayloads)
            {
                LLMDecisionLog.Debug("PAYLOAD", "chat_completions request=" + snapshotJson);
            }

            LLMHttpResult http = null;
            yield return Send(endpoint, JsonConvert.SerializeObject(payload, Formatting.None), settings,
                onRequestCreated, result => http = result);
            if (!http.Success)
            {
                if (useJsonResponseFormat && IsChatJsonFormatUnsupported(http.Status, http.Error))
                {
                    LLMDecisionLog.Warning(
                        "API",
                        $"Chat Completions HTTP {http.Status} rejected response_format; retrying once without it");
                    yield return RequestChat(settings, snapshot, endpoint, onRequestCreated, onComplete,
                        fallbackReason, false);
                    yield break;
                }
                onComplete(Failure(http, "chat_completions", fallbackReason));
                yield break;
            }

            try
            {
                OpenAIChatResponse response = JsonConvert.DeserializeObject<OpenAIChatResponse>(http.Body);
                if (response?.Error != null)
                {
                    onComplete(new LLMApiResult
                    {
                        Success = false,
                        Error = "api_error:" + (response.Error.Message ?? response.Error.Type ?? "unknown"),
                        HttpStatus = http.Status,
                        ApiMode = "chat_completions",
                        ResponseId = response.Id,
                        Usage = response.Usage,
                        FallbackReason = fallbackReason
                    });
                    yield break;
                }
                string content = response?.Choices != null && response.Choices.Count > 0
                    ? response.Choices[0]?.Message?.Content
                    : null;
                if (string.IsNullOrWhiteSpace(content))
                {
                    onComplete(new LLMApiResult
                    {
                        Success = false,
                        Error = "empty_response",
                        HttpStatus = http.Status,
                        ApiMode = "chat_completions",
                        ResponseId = response?.Id,
                        Usage = response?.Usage,
                        FallbackReason = fallbackReason
                    });
                    yield break;
                }
                if (settings.DebugLogPayloads)
                {
                    LLMDecisionLog.Debug("PAYLOAD", "chat_completions response=" + content);
                }
                onComplete(new LLMApiResult
                {
                    Success = true,
                    Content = content,
                    HttpStatus = http.Status,
                    ApiMode = "chat_completions",
                    ResponseId = response?.Id,
                    Usage = response?.Usage,
                    FallbackReason = fallbackReason
                });
            }
            catch (Exception exception)
            {
                onComplete(new LLMApiResult
                {
                    Success = false,
                    Error = "invalid_api_envelope:" + exception.Message,
                    HttpStatus = http.Status,
                    ApiMode = "chat_completions",
                    FallbackReason = fallbackReason
                });
            }
        }

        private static IEnumerator Send(string endpoint, string requestJson, LLMAISettings settings,
            Action<UnityWebRequest> onRequestCreated, Action<LLMHttpResult> onComplete)
        {
            using (UnityWebRequest request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(requestJson));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = Math.Max(1, (int)Math.Ceiling(settings.TimeoutSeconds));
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", "Bearer " + settings.ApiKey);
                onRequestCreated?.Invoke(request);
                yield return request.SendWebRequest();

                string body = request.downloadHandler?.text;
                bool success = request.result == UnityWebRequest.Result.Success;
                string error = success ? null : TryReadApiError(body) ?? request.error ?? "request_failed";
                onComplete(new LLMHttpResult
                {
                    Success = success,
                    Status = request.responseCode,
                    Body = body,
                    Error = error,
                    TimedOut = !success && !string.IsNullOrEmpty(request.error) &&
                               request.error.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0
                });
            }
        }

        private static bool IsChatJsonFormatUnsupported(long status, string message)
        {
            if ((status != 400 && status != 422) || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }
            string value = message.ToLowerInvariant();
            return value.Contains("response_format") &&
                   (value.Contains("unsupported") || value.Contains("unknown") || value.Contains("not support"));
        }

        private static OpenAIReasoning CreateReasoning(string effort)
        {
            string normalized = NormalizeEffort(effort);
            return normalized == null ? null : new OpenAIReasoning { Effort = normalized };
        }

        private static string NormalizeEffort(string effort)
        {
            return string.IsNullOrWhiteSpace(effort) ? null : effort.Trim().ToLowerInvariant();
        }

        private static string BuildPrompt(string promptFile)
        {
            return LoadSystemPrompt(promptFile) +
                   " The goal must be exactly one of lethal, combo, tempo, defend, setup.";
        }

        private static string LoadSystemPrompt(string promptFile)
        {
            if (string.IsNullOrWhiteSpace(promptFile))
            {
                return DefaultSystemPrompt;
            }
            try
            {
                string path = Path.IsPathRooted(promptFile)
                    ? promptFile
                    : Path.Combine(Paths.GameRootPath, promptFile);
                return File.Exists(path) ? File.ReadAllText(path) : DefaultSystemPrompt;
            }
            catch (Exception exception)
            {
                LLMDecisionLog.Warning(
                    "PROMPT",
                    "failed to read prompt file; using built-in prompt: " + exception.Message);
                return DefaultSystemPrompt;
            }
        }

        private static LLMApiResult Failure(LLMHttpResult http, string apiMode, string fallbackReason)
        {
            return new LLMApiResult
            {
                Success = false,
                Error = http.Error,
                HttpStatus = http.Status,
                ApiMode = apiMode,
                FallbackReason = fallbackReason
            };
        }

        private static string TryReadApiError(string responseText)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return null;
            }
            try
            {
                ErrorEnvelope response = JsonConvert.DeserializeObject<ErrorEnvelope>(responseText);
                return response?.Error?.Message ?? response?.Error?.Type ?? response?.Error?.Code;
            }
            catch
            {
                return null;
            }
        }

        private static string Safe(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            return value.Length <= 240 ? value : value.Substring(0, 240);
        }

        private sealed class LLMHttpResult
        {
            public bool Success;
            public long Status;
            public string Body;
            public string Error;
            public bool TimedOut;
        }

        private sealed class ErrorEnvelope
        {
            [JsonProperty("error")]
            public OpenAIError Error;
        }
    }
}
