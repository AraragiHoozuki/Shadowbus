using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Shadowbus.LLMAI
{
    internal enum LLMApiMode
    {
        Auto,
        Responses,
        ChatCompletions
    }

    internal sealed class LLMAISettings
    {
        public bool Enabled;
        public string Endpoint;
        public string ResponsesEndpoint;
        public string ChatCompletionsEndpoint;
        public LLMApiMode ApiMode = LLMApiMode.Auto;
        public string ApiKey;
        public string Model;
        public string ReasoningEffort = "high";
        public float TimeoutSeconds;
        public int MaxCandidates;
        public int MaxPlanSteps;
        public int MaxApiCallsPerTurn;
        public int MaxResponseTokens;
        public int MaxOutputTokens = 4096;
        public int LethalSearchMaxPatterns = 32;
        public int LethalSearchBudgetMs = 1000;
        public string PromptFile;
        public bool DebugLogPayloads;

        internal bool IsUsable(out string reason)
        {
            LLMEndpointSet endpoints = LLMEndpointResolver.Resolve(this);
            string endpointValue = ApiMode == LLMApiMode.ChatCompletions
                ? endpoints.ChatCompletions
                : endpoints.Responses;
            if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri endpoint) ||
                (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
            {
                reason = "invalid_endpoint";
                return false;
            }
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                reason = "missing_api_key";
                return false;
            }
            if (string.IsNullOrWhiteSpace(Model))
            {
                reason = "missing_model";
                return false;
            }
            if (!LLMEndpointResolver.IsValidReasoningEffort(ReasoningEffort))
            {
                reason = "invalid_reasoning_effort";
                return false;
            }
            reason = null;
            return true;
        }
    }

    internal sealed class LLMEndpointSet
    {
        public string Responses;
        public string ChatCompletions;
    }

    internal static class LLMEndpointResolver
    {
        internal static bool TryParseMode(string value, out LLMApiMode mode)
        {
            return Enum.TryParse(value ?? string.Empty, true, out mode) &&
                   Enum.IsDefined(typeof(LLMApiMode), mode);
        }

        internal static bool IsValidReasoningEffort(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return true;
            }
            switch (value.Trim().ToLowerInvariant())
            {
                case "none":
                case "minimal":
                case "low":
                case "medium":
                case "high":
                case "xhigh":
                    return true;
                default:
                    return false;
            }
        }

        internal static LLMEndpointSet Resolve(LLMAISettings settings)
        {
            string endpoint = (settings?.Endpoint ?? string.Empty).Trim();
            string responses = Derive(endpoint, true);
            string chat = Derive(endpoint, false);
            if (!string.IsNullOrWhiteSpace(settings?.ResponsesEndpoint))
            {
                responses = settings.ResponsesEndpoint.Trim();
            }
            if (!string.IsNullOrWhiteSpace(settings?.ChatCompletionsEndpoint))
            {
                chat = settings.ChatCompletionsEndpoint.Trim();
            }
            return new LLMEndpointSet { Responses = responses, ChatCompletions = chat };
        }

        private static string Derive(string endpoint, bool responses)
        {
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri uri))
            {
                return endpoint;
            }
            string path = uri.AbsolutePath.TrimEnd('/');
            if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/chat/completions".Length) +
                       (responses ? "/responses" : "/chat/completions");
            }
            else if (path.EndsWith("/responses", StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(0, path.Length - "/responses".Length) +
                       (responses ? "/responses" : "/chat/completions");
            }
            else
            {
                path += responses ? "/responses" : "/chat/completions";
            }
            UriBuilder builder = new UriBuilder(uri) { Path = path };
            return builder.Uri.AbsoluteUri;
        }
    }

    internal static class LLMApiFallbackPolicy
    {
        internal static bool IsResponsesTextFormatUnsupported(long status, string message)
        {
            if ((status != 400 && status != 422) || string.IsNullOrWhiteSpace(message))
            {
                return false;
            }
            string value = message.ToLowerInvariant();
            return (value.Contains("text.format") || value.Contains("json_schema")) &&
                   (value.Contains("unsupported") || value.Contains("unknown") || value.Contains("not support") ||
                    value.Contains("unrecognized"));
        }

        internal static bool ShouldFallbackToChat(long status, string message, bool timedOut)
        {
            if (timedOut || status == 0 || status == 401 || status == 403 || status == 408 ||
                status == 429 || status >= 500)
            {
                return false;
            }
            if (status == 404 || status == 405 || status == 415 || status == 501)
            {
                return true;
            }
            if ((status == 400 || status == 422) && !string.IsNullOrWhiteSpace(message))
            {
                string value = message.ToLowerInvariant();
                return (value.Contains("instructions") || value.Contains("max_output_tokens") ||
                        value.Contains("responses api") || value.Contains("unknown endpoint")) &&
                       (value.Contains("unknown") || value.Contains("unsupported") ||
                        value.Contains("unrecognized") || value.Contains("not support"));
            }
            return false;
        }
    }

    internal sealed class TurnPlan
    {
        [JsonProperty("state_hash", Required = Required.Always)]
        public string StateHash;

        [JsonProperty("goal", Required = Required.Always)]
        public string Goal;

        [JsonProperty("reason")]
        public string Reason;

        [JsonProperty("steps", Required = Required.Always)]
        public List<TurnPlanStep> Steps;
    }

    internal sealed class TurnPlanStep
    {
        [JsonProperty("step_id", Required = Required.Always)]
        public string StepId;

        [JsonProperty("type", Required = Required.Always)]
        public string Type;

        [JsonProperty("actor")]
        public string Actor;

        [JsonProperty("mode")]
        public string Mode;

        [JsonProperty("targets")]
        public List<string> Targets;

        [JsonProperty("replan_after")]
        public bool ReplanAfter;
    }

    internal sealed class LegalActionDto
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("actor")]
        public string Actor;

        [JsonProperty("mode")]
        public string Mode;

        [JsonProperty("targets")]
        public List<string> Targets;

        [JsonProperty("pp_cost")]
        public int PpCost;

        [JsonProperty("pp_after")]
        public int PpAfter;

        [JsonProperty("ep_cost")]
        public int EpCost;

        [JsonProperty("draw_count")]
        public int DrawCount;

        [JsonProperty("reveals_hidden_information")]
        public bool RevealsHiddenInformation;

        [JsonProperty("requires_replan_after")]
        public bool RequiresReplanAfter;
    }

    internal sealed class BattleSnapshotDto
    {
        [JsonProperty("state_hash")]
        public string StateHash;

        [JsonProperty("turn")]
        public int Turn;

        [JsonProperty("self")]
        public PlayerSnapshotDto Self;

        [JsonProperty("opponent")]
        public PlayerSnapshotDto Opponent;

        [JsonProperty("legal_actions")]
        public List<LegalActionDto> LegalActions;

        [JsonProperty("verified_lethal_macros")]
        public List<object> VerifiedLethalMacros = new List<object>();

        [JsonProperty("continuation", NullValueHandling = NullValueHandling.Ignore)]
        public PlanContinuationDto Continuation;
    }

    internal sealed class PlayerSnapshotDto
    {
        [JsonProperty("turn")]
        public int Turn;

        [JsonProperty("life")]
        public int Life;

        [JsonProperty("pp")]
        public int Pp;

        [JsonProperty("pp_total")]
        public int PpTotal;

        [JsonProperty("ep")]
        public int Ep;

        [JsonProperty("ep_total")]
        public int EpTotal;

        [JsonProperty("ep_used_game")]
        public int EpUsedGame;

        [JsonProperty("ep_used_turn")]
        public int EpUsedTurn;

        [JsonProperty("can_evolve")]
        public bool CanEvolve;

        [JsonProperty("deck_count")]
        public int DeckCount;

        [JsonProperty("deck_summary", NullValueHandling = NullValueHandling.Ignore)]
        public List<DeckCardSummaryDto> DeckSummary;

        [JsonProperty("hand_count")]
        public int HandCount;

        [JsonProperty("hand", NullValueHandling = NullValueHandling.Ignore)]
        public List<CardSnapshotDto> Hand;

        [JsonProperty("board")]
        public List<CardSnapshotDto> Board;

        [JsonProperty("cemetery_count")]
        public int CemeteryCount;

        [JsonProperty("evolved_game")]
        public int EvolvedGame;

        [JsonProperty("evolved_previous_turn")]
        public int EvolvedPreviousTurn;

        [JsonProperty("cards_played_turn")]
        public int CardsPlayedTurn;

        [JsonProperty("cards_played_game")]
        public int CardsPlayedGame;

        [JsonProperty("rally")]
        public int Rally;

        [JsonProperty("cemetery_consumed")]
        public int CemeteryConsumed;

        [JsonProperty("cards_drawn_turn")]
        public int CardsDrawnTurn;

        [JsonProperty("cards_drawn_game")]
        public int CardsDrawnGame;

        [JsonProperty("resonance_turn")]
        public int ResonanceTurn;

        [JsonProperty("resonance_game")]
        public int ResonanceGame;

        [JsonProperty("fusion_turn")]
        public int FusionTurn;

        [JsonProperty("fusion_game")]
        public int FusionGame;

        [JsonProperty("burial_rite_turn")]
        public int BurialRiteTurn;

        [JsonProperty("burial_rite_game")]
        public int BurialRiteGame;

        [JsonProperty("damage_count_game")]
        public int DamageCountGame;

        [JsonProperty("damage_count_turn")]
        public int DamageCountTurn;

        [JsonProperty("pp_used_game")]
        public int PpUsedGame;
    }

    internal sealed class DeckCardSummaryDto
    {
        [JsonProperty("base_card_id")]
        public int BaseCardId;

        [JsonProperty("count")]
        public int Count;

        [JsonProperty("cost")]
        public int Cost;

        [JsonProperty("name")]
        public string Name;

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text;
    }

    internal sealed class CardSnapshotDto
    {
        [JsonProperty("ref")]
        public string Ref;

        [JsonProperty("base_card_id")]
        public int BaseCardId;

        [JsonProperty("name")]
        public string Name;

        [JsonProperty("cost")]
        public int Cost;

        [JsonProperty("attack")]
        public int Attack;

        [JsonProperty("life")]
        public int Life;

        [JsonProperty("evolved")]
        public bool Evolved;

        [JsonProperty("evolution_attack")]
        public int EvolutionAttack;

        [JsonProperty("evolution_life")]
        public int EvolutionLife;

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text;

        [JsonProperty("evolved_text", NullValueHandling = NullValueHandling.Ignore)]
        public string EvolvedText;

        [JsonProperty("spellboost")]
        public int Spellboost;

        [JsonProperty("countdown")]
        public int Countdown;

        [JsonProperty("stack")]
        public int Stack;

        [JsonProperty("union_burst_count")]
        public int UnionBurstCount;

        [JsonProperty("skybound_art_count")]
        public int SkyboundArtCount;

        [JsonProperty("damaged_count")]
        public int DamagedCount;

        [JsonProperty("can_evolve")]
        public bool CanEvolve;

        [JsonProperty("evolve_consumes_ep")]
        public bool EvolveConsumesEp;
    }

    internal sealed class PlanContinuationDto
    {
        [JsonProperty("goal")]
        public string Goal;

        [JsonProperty("executed_steps")]
        public List<TurnPlanStep> ExecutedSteps;

        [JsonProperty("replan_reason")]
        public string ReplanReason;
    }

    internal sealed class OpenAIChatRequest
    {
        [JsonProperty("model")]
        public string Model;

        [JsonProperty("messages")]
        public List<OpenAIChatMessage> Messages;

        [JsonProperty("temperature")]
        public float Temperature = 0f;

        [JsonProperty("max_tokens")]
        public int MaxTokens;

        [JsonProperty("reasoning_effort", NullValueHandling = NullValueHandling.Ignore)]
        public string ReasoningEffort;

        [JsonProperty("response_format", NullValueHandling = NullValueHandling.Ignore)]
        public object ResponseFormat = new { type = "json_object" };
    }

    internal sealed class OpenAIChatMessage
    {
        [JsonProperty("role")]
        public string Role;

        [JsonProperty("content")]
        public string Content;
    }

    internal sealed class OpenAIChatResponse
    {
        [JsonProperty("choices")]
        public List<OpenAIChatChoice> Choices;

        [JsonProperty("error")]
        public OpenAIError Error;

        [JsonProperty("id")]
        public string Id;

        [JsonProperty("usage")]
        public OpenAIUsage Usage;
    }

    internal sealed class OpenAIChatChoice
    {
        [JsonProperty("message")]
        public OpenAIChatMessage Message;
    }

    internal sealed class OpenAIError
    {
        [JsonProperty("message")]
        public string Message;

        [JsonProperty("type")]
        public string Type;

        [JsonProperty("code")]
        public string Code;
    }

    internal sealed class OpenAIResponsesRequest
    {
        [JsonProperty("model")]
        public string Model;

        [JsonProperty("instructions")]
        public string Instructions;

        [JsonProperty("input")]
        public string Input;

        [JsonProperty("max_output_tokens")]
        public int MaxOutputTokens;

        [JsonProperty("reasoning", NullValueHandling = NullValueHandling.Ignore)]
        public OpenAIReasoning Reasoning;

        [JsonProperty("store")]
        public bool Store;

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public OpenAITextConfiguration Text;
    }

    internal sealed class OpenAIReasoning
    {
        [JsonProperty("effort")]
        public string Effort;
    }

    internal sealed class OpenAITextConfiguration
    {
        [JsonProperty("format")]
        public object Format;
    }

    internal sealed class OpenAIResponsesEnvelope
    {
        [JsonProperty("id")]
        public string Id;

        [JsonProperty("status")]
        public string Status;

        [JsonProperty("output")]
        public List<OpenAIResponseOutput> Output;

        [JsonProperty("incomplete_details")]
        public OpenAIIncompleteDetails IncompleteDetails;

        [JsonProperty("usage")]
        public OpenAIUsage Usage;

        [JsonProperty("error")]
        public OpenAIError Error;
    }

    internal sealed class OpenAIResponseOutput
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("content")]
        public List<OpenAIResponseContent> Content;
    }

    internal sealed class OpenAIResponseContent
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("text")]
        public string Text;

        [JsonProperty("refusal")]
        public string Refusal;
    }

    internal sealed class OpenAIIncompleteDetails
    {
        [JsonProperty("reason")]
        public string Reason;
    }

    internal sealed class OpenAIUsage
    {
        [JsonProperty("input_tokens")]
        public int InputTokens;

        [JsonProperty("output_tokens")]
        public int OutputTokens;

        [JsonProperty("total_tokens")]
        public int TotalTokens;

        [JsonProperty("prompt_tokens")]
        public int PromptTokens;

        [JsonProperty("completion_tokens")]
        public int CompletionTokens;
    }

    internal static class OpenAIResponsesProtocol
    {
        internal static object CreateTurnPlanFormat()
        {
            JObject step = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["step_id"] = new JObject { ["type"] = "string" },
                    ["type"] = new JObject { ["type"] = "string", ["enum"] = new JArray("play", "attack", "evolve", "fusion", "turn_end") },
                    ["actor"] = new JObject { ["type"] = new JArray("string", "null") },
                    ["mode"] = new JObject { ["type"] = new JArray("string", "null") },
                    ["targets"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } },
                    ["replan_after"] = new JObject { ["type"] = "boolean" }
                },
                ["required"] = new JArray("step_id", "type", "actor", "mode", "targets", "replan_after")
            };
            JObject schema = new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["state_hash"] = new JObject { ["type"] = "string" },
                    ["goal"] = new JObject { ["type"] = "string", ["enum"] = new JArray("lethal", "combo", "tempo", "defend", "setup") },
                    ["reason"] = new JObject { ["type"] = new JArray("string", "null") },
                    ["steps"] = new JObject { ["type"] = "array", ["items"] = step }
                },
                ["required"] = new JArray("state_hash", "goal", "reason", "steps")
            };
            return new JObject
            {
                ["type"] = "json_schema",
                ["name"] = "turn_plan",
                ["strict"] = true,
                ["schema"] = schema
            };
        }

        internal static bool TryExtract(string json, out string content, out string responseId,
            out OpenAIUsage usage, out string error)
        {
            content = null;
            responseId = null;
            usage = null;
            error = null;
            try
            {
                OpenAIResponsesEnvelope envelope = JsonConvert.DeserializeObject<OpenAIResponsesEnvelope>(json);
                responseId = envelope?.Id;
                usage = envelope?.Usage;
                if (envelope?.Error != null)
                {
                    error = "api_error:" + (envelope.Error.Message ?? envelope.Error.Type ?? envelope.Error.Code ?? "unknown");
                    return false;
                }
                if (string.Equals(envelope?.Status, "incomplete", StringComparison.OrdinalIgnoreCase))
                {
                    error = "incomplete_response:" + (envelope.IncompleteDetails?.Reason ?? "unknown");
                    return false;
                }
                foreach (OpenAIResponseOutput output in envelope?.Output ?? new List<OpenAIResponseOutput>())
                {
                    foreach (OpenAIResponseContent item in output?.Content ?? new List<OpenAIResponseContent>())
                    {
                        if (string.Equals(item?.Type, "refusal", StringComparison.OrdinalIgnoreCase))
                        {
                            error = "refusal:" + (item.Refusal ?? "unspecified");
                            return false;
                        }
                        if (string.Equals(item?.Type, "output_text", StringComparison.OrdinalIgnoreCase) &&
                            !string.IsNullOrWhiteSpace(item.Text))
                        {
                            content = item.Text;
                            return true;
                        }
                    }
                }
                error = "empty_response";
                return false;
            }
            catch (Exception exception)
            {
                error = "invalid_api_envelope:" + exception.Message;
                return false;
            }
        }
    }

    internal static class TurnPlanParser
    {
        private static readonly HashSet<string> Goals =
            new HashSet<string>(StringComparer.Ordinal) { "lethal", "combo", "tempo", "defend", "setup" };

        private static readonly HashSet<string> Types =
            new HashSet<string>(StringComparer.Ordinal) { "play", "attack", "evolve", "fusion", "turn_end" };

        private static readonly HashSet<string> Modes =
            new HashSet<string>(StringComparer.Ordinal) { "normal", "enhance", "accelerate", "crystalize", "choice_transform", "fusion" };

        internal static bool TryParse(string json, int maxSteps, out TurnPlan plan, out string reason)
        {
            plan = null;
            reason = null;
            try
            {
                JsonSerializerSettings settings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Error,
                    NullValueHandling = NullValueHandling.Include
                };
                plan = JsonConvert.DeserializeObject<TurnPlan>(json, settings);
            }
            catch (Exception exception)
            {
                reason = "invalid_json:" + exception.Message;
                return false;
            }

            if (plan == null || string.IsNullOrWhiteSpace(plan.StateHash))
            {
                reason = "missing_state_hash";
                return false;
            }
            if (!Goals.Contains(plan.Goal ?? string.Empty))
            {
                reason = "invalid_goal";
                return false;
            }
            if (plan.Steps == null || plan.Steps.Count == 0 || plan.Steps.Count > maxSteps)
            {
                reason = "invalid_step_count";
                return false;
            }

            HashSet<string> stepIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < plan.Steps.Count; i++)
            {
                TurnPlanStep step = plan.Steps[i];
                if (step == null || string.IsNullOrWhiteSpace(step.StepId) || !stepIds.Add(step.StepId))
                {
                    reason = $"step_{i}:invalid_step_id";
                    return false;
                }
                if (!Types.Contains(step.Type ?? string.Empty))
                {
                    reason = $"step_{i}:invalid_type";
                    return false;
                }
                step.Targets ??= new List<string>();
                if (step.Type == "turn_end")
                {
                    if (!string.IsNullOrEmpty(step.Actor) || step.Targets.Count != 0 || !string.IsNullOrEmpty(step.Mode))
                    {
                        reason = $"step_{i}:invalid_turn_end_shape";
                        return false;
                    }
                }
                else if (string.IsNullOrWhiteSpace(step.Actor))
                {
                    reason = $"step_{i}:missing_actor";
                    return false;
                }
                if ((step.Type == "play" || step.Type == "fusion") && !Modes.Contains(step.Mode ?? string.Empty))
                {
                    reason = $"step_{i}:invalid_mode";
                    return false;
                }
                if (step.ReplanAfter && i != plan.Steps.Count - 1)
                {
                    reason = $"step_{i}:replan_after_must_end_plan";
                    return false;
                }
            }
            return true;
        }
    }
}
