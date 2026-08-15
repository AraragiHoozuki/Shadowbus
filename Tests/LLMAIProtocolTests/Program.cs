using Newtonsoft.Json;
using Shadowbus.LLMAI;
using System;
using System.Collections.Generic;

internal static class Program
{
    private static int _passed;

    private static void Main()
    {
        AcceptsMultiStepPlan();
        RejectsUnknownProperties();
        RejectsDuplicateStepIds();
        RejectsNonTerminalReplanBoundary();
        RejectsMalformedTurnEnd();
        RejectsOversizedPlan();
        OmitsOpponentHandIdentities();
        OmitsNullResponseFormat();
        SerializesEvolutionStats();
        SerializesResponsesSchemaAndReasoning();
        ExtractsResponsesOutputText();
        RejectsResponsesRefusalAndTruncation();
        ParsesLegacyChatResponse();
        DerivesResponsesFromLegacyChatEndpoint();
        AppliesStrictFallbackPolicy();
        SerializesActionCostsAndRuleCounters();
        Console.WriteLine($"LLMAI protocol tests passed: {_passed}");
    }

    private static void AcceptsMultiStepPlan()
    {
        const string json = @"{
          'state_hash':'abc', 'goal':'combo', 'reason':'test',
          'steps':[
            {'step_id':'s1','type':'play','actor':'card:self:4','mode':'accelerate','targets':['card:opponent:2']},
            {'step_id':'s2','type':'attack','actor':'card:self:8','targets':['opponent_leader']},
            {'step_id':'s3','type':'turn_end','targets':[]}
          ]}";
        Assert(TurnPlanParser.TryParse(json, 12, out TurnPlan plan, out _), "valid plan rejected");
        Assert(plan.Steps.Count == 3 && plan.Steps[0].Mode == "accelerate", "valid plan changed");
        Pass();
    }

    private static void RejectsUnknownProperties()
    {
        const string json = "{'state_hash':'abc','goal':'tempo','unexpected':1,'steps':[{'step_id':'s1','type':'turn_end'}]}";
        Assert(!TurnPlanParser.TryParse(json, 12, out _, out string reason) && reason.StartsWith("invalid_json:"),
            "unknown property accepted");
        Pass();
    }

    private static void RejectsDuplicateStepIds()
    {
        const string json = "{'state_hash':'abc','goal':'tempo','steps':[{'step_id':'s1','type':'attack','actor':'card:self:1','targets':['opponent_leader']},{'step_id':'s1','type':'turn_end'}]}";
        Assert(!TurnPlanParser.TryParse(json, 12, out _, out string reason) && reason.Contains("invalid_step_id"),
            "duplicate step id accepted");
        Pass();
    }

    private static void RejectsNonTerminalReplanBoundary()
    {
        const string json = "{'state_hash':'abc','goal':'setup','steps':[{'step_id':'s1','type':'play','actor':'card:self:1','mode':'normal','targets':[],'replan_after':true},{'step_id':'s2','type':'turn_end'}]}";
        Assert(!TurnPlanParser.TryParse(json, 12, out _, out string reason) && reason.Contains("replan_after_must_end_plan"),
            "non-terminal replan accepted");
        Pass();
    }

    private static void RejectsMalformedTurnEnd()
    {
        const string json = "{'state_hash':'abc','goal':'tempo','steps':[{'step_id':'s1','type':'turn_end','actor':'self_leader'}]}";
        Assert(!TurnPlanParser.TryParse(json, 12, out _, out string reason) && reason.Contains("invalid_turn_end_shape"),
            "turn_end actor accepted");
        Pass();
    }

    private static void RejectsOversizedPlan()
    {
        const string json = "{'state_hash':'abc','goal':'tempo','steps':[{'step_id':'s1','type':'turn_end'}]}";
        Assert(!TurnPlanParser.TryParse(json, 0, out _, out string reason) && reason == "invalid_step_count",
            "oversized plan accepted");
        Pass();
    }

    private static void OmitsOpponentHandIdentities()
    {
        BattleSnapshotDto snapshot = new BattleSnapshotDto
        {
            StateHash = "hash",
            Self = new PlayerSnapshotDto { Hand = new List<CardSnapshotDto>() },
            Opponent = new PlayerSnapshotDto { HandCount = 4, Hand = null },
            LegalActions = new List<LegalActionDto>()
        };
        string json = JsonConvert.SerializeObject(snapshot);
        Assert(json.Contains("\"hand_count\":4") && !json.Contains("base_card_id"),
            "opponent hidden hand leaked");
        Pass();
    }

    private static void OmitsNullResponseFormat()
    {
        OpenAIChatRequest request = new OpenAIChatRequest
        {
            ResponseFormat = null
        };
        string json = JsonConvert.SerializeObject(request);
        Assert(!json.Contains("response_format"), "null response_format was serialized");
        Pass();
    }

    private static void SerializesEvolutionStats()
    {
        CardSnapshotDto card = new CardSnapshotDto
        {
            EvolutionAttack = 5,
            EvolutionLife = 7
        };
        string json = JsonConvert.SerializeObject(card);
        Assert(json.Contains("\"evolution_attack\":5") && json.Contains("\"evolution_life\":7"),
            "evolution stats were omitted");
        Pass();
    }

    private static void SerializesResponsesSchemaAndReasoning()
    {
        OpenAIResponsesRequest request = new OpenAIResponsesRequest
        {
            Model = "test-model",
            Instructions = "decide",
            Input = "{}",
            MaxOutputTokens = 4096,
            Reasoning = new OpenAIReasoning { Effort = "high" },
            Store = false,
            Text = new OpenAITextConfiguration { Format = OpenAIResponsesProtocol.CreateTurnPlanFormat() }
        };
        string json = JsonConvert.SerializeObject(request);
        Assert(json.Contains("\"max_output_tokens\":4096") &&
               json.Contains("\"reasoning\":{\"effort\":\"high\"}") &&
               json.Contains("\"store\":false") &&
               json.Contains("\"type\":\"json_schema\"") &&
               json.Contains("\"strict\":true") &&
               json.Contains("\"additionalProperties\":false") &&
               !json.Contains("temperature"),
            "Responses request or strict TurnPlan schema is incomplete");
        Pass();
    }

    private static void ExtractsResponsesOutputText()
    {
        const string json = @"{
          'id':'resp_123','status':'completed',
          'output':[{'type':'message','content':[{'type':'output_text','text':'{""state_hash"":""abc""}'}]}],
          'usage':{'input_tokens':10,'output_tokens':20,'total_tokens':30}
        }";
        Assert(OpenAIResponsesProtocol.TryExtract(json, out string content, out string id,
                   out OpenAIUsage usage, out string error) &&
               id == "resp_123" && content.Contains("state_hash") && usage.TotalTokens == 30 && error == null,
            "Responses output_text was not extracted");
        Pass();
    }

    private static void RejectsResponsesRefusalAndTruncation()
    {
        const string refusal = "{'id':'resp_refused','status':'completed','output':[{'content':[{'type':'refusal','refusal':'no'}]}]}";
        Assert(!OpenAIResponsesProtocol.TryExtract(refusal, out _, out _, out _, out string refusalError) &&
               refusalError == "refusal:no", "Responses refusal was accepted");

        const string incomplete = "{'id':'resp_short','status':'incomplete','incomplete_details':{'reason':'max_output_tokens'}}";
        Assert(!OpenAIResponsesProtocol.TryExtract(incomplete, out _, out _, out _, out string incompleteError) &&
               incompleteError == "incomplete_response:max_output_tokens", "incomplete Responses envelope was accepted");
        Pass();
    }

    private static void ParsesLegacyChatResponse()
    {
        const string json = "{'id':'chat_1','choices':[{'message':{'role':'assistant','content':'{}'}}]," +
                            "'usage':{'prompt_tokens':3,'completion_tokens':4,'total_tokens':7}}";
        OpenAIChatResponse response = JsonConvert.DeserializeObject<OpenAIChatResponse>(json);
        Assert(response.Id == "chat_1" && response.Choices[0].Message.Content == "{}" &&
               response.Usage.TotalTokens == 7, "legacy Chat Completions response changed");
        Pass();
    }

    private static void DerivesResponsesFromLegacyChatEndpoint()
    {
        LLMAISettings settings = new LLMAISettings
        {
            Endpoint = "https://example.test/v1/chat/completions"
        };
        LLMEndpointSet endpoints = LLMEndpointResolver.Resolve(settings);
        Assert(endpoints.Responses == "https://example.test/v1/responses" &&
               endpoints.ChatCompletions == "https://example.test/v1/chat/completions",
            "legacy chat endpoint did not derive Responses first");

        settings.ResponsesEndpoint = "https://responses.example.test/custom";
        Assert(LLMEndpointResolver.Resolve(settings).Responses == "https://responses.example.test/custom",
            "ResponsesEndpoint override was ignored");
        Pass();
    }

    private static void AppliesStrictFallbackPolicy()
    {
        Assert(LLMApiFallbackPolicy.IsResponsesTextFormatUnsupported(400,
                   "text.format json_schema is unsupported"),
            "explicit text.format incompatibility was not detected");
        Assert(LLMApiFallbackPolicy.ShouldFallbackToChat(404, "not found", false),
            "endpoint incompatibility did not fall back");
        Assert(LLMApiFallbackPolicy.ShouldFallbackToChat(400, "Unknown parameter: instructions", false),
            "Responses field incompatibility did not fall back");
        Assert(!LLMApiFallbackPolicy.ShouldFallbackToChat(401, "unauthorized", false) &&
               !LLMApiFallbackPolicy.ShouldFallbackToChat(429, "rate limited", false) &&
               !LLMApiFallbackPolicy.ShouldFallbackToChat(500, "server error", false) &&
               !LLMApiFallbackPolicy.ShouldFallbackToChat(0, "timed out", true),
            "auth, rate limit, timeout, or 5xx incorrectly fell back");
        Pass();
    }

    private static void SerializesActionCostsAndRuleCounters()
    {
        BattleSnapshotDto snapshot = new BattleSnapshotDto
        {
            Self = new PlayerSnapshotDto
            {
                EpTotal = 3,
                EpUsedGame = 1,
                CanEvolve = true,
                CardsPlayedTurn = 4,
                EvolvedGame = 2,
                DeckSummary = new List<DeckCardSummaryDto>
                {
                    new DeckCardSummaryDto { BaseCardId = 10, Count = 2, Cost = 1, Name = "Draw" }
                },
                Hand = new List<CardSnapshotDto>
                {
                    new CardSnapshotDto { Spellboost = 5, Countdown = 2, CanEvolve = true }
                }
            },
            LegalActions = new List<LegalActionDto>
            {
                new LegalActionDto
                {
                    Type = "play", PpCost = 1, PpAfter = 2, DrawCount = 1,
                    RevealsHiddenInformation = true, RequiresReplanAfter = true
                }
            }
        };
        string json = JsonConvert.SerializeObject(snapshot);
        Assert(json.Contains("\"pp_cost\":1") && json.Contains("\"pp_after\":2") &&
               json.Contains("\"draw_count\":1") && json.Contains("\"requires_replan_after\":true") &&
               json.Contains("\"ep_used_game\":1") && json.Contains("\"cards_played_turn\":4") &&
               json.Contains("\"deck_summary\"") && json.Contains("\"spellboost\":5"),
            "action costs or public rule counters were omitted");
        Pass();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Pass()
    {
        _passed++;
    }
}
