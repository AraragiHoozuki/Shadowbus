using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shadowbus.LLMAI
{
    internal static class LLMDecisionLog
    {
        private const string Prefix = "[LLM AI]";

        internal static void Configuration(LLMAISettings settings, LLMEndpointSet endpoints)
        {
            StringBuilder text = Begin("READY", "LLM opponent controller enabled");
            Line(text, "API", settings.ApiMode + "  reasoning=" + Display(settings.ReasoningEffort, "omitted"));
            Line(text, "MODEL", settings.Model);
            Line(text, "RESP", endpoints.Responses);
            Line(text, "CHAT", endpoints.ChatCompletions);
            End(text, $"timeout={settings.TimeoutSeconds:0.#}s  plans={settings.MaxPlanSteps} steps  calls={settings.MaxApiCallsPerTurn}");
            Write(LogLevel.Message, text);
        }

        internal static void TurnStart(int turn, string stateHash, int legalActions, string continuation)
        {
            StringBuilder text = Begin("TURN " + turn, "decision point");
            Line(text, "STATE", ShortHash(stateHash));
            Line(text, "LEGAL", legalActions + " actions");
            End(text, "continuation=" + Display(continuation, "none"));
            Write(LogLevel.Message, text);
        }

        internal static void Request(int turn, int call, string stateHash, int legalActions, string continuation)
        {
            Write(LogLevel.Info,
                $"{Prefix} -> MODEL  turn={turn}  call={call}  state={ShortHash(stateHash)}  " +
                $"legal={legalActions}  continuation={Display(continuation, "none")}");
        }

        internal static void Response(int turn, int call, LLMApiResult result, string reasoning, string usage)
        {
            StringBuilder text = Begin("RESPONSE", $"turn {turn} / call {call}");
            Line(text, "API", Display(result?.ApiMode, "unknown") + "  HTTP " + (result?.HttpStatus ?? 0));
            Line(text, "ID", Display(result?.ResponseId, "not supplied"));
            Line(text, "TOKENS", usage + "  reasoning=" + Display(reasoning, "omitted"));
            End(text, "fallback=" + Display(result?.FallbackReason, "none"));
            Write(LogLevel.Info, text);
        }

        internal static void Plan(int turn, int number, TurnPlan plan, CompiledTurnPlan compiled)
        {
            string goal = (plan?.Goal ?? "unknown").ToUpperInvariant();
            StringBuilder text = Begin("PLAN " + number, goal);
            Line(text, "WHY", Clean(plan?.Reason, 180));
            int count = compiled?.Steps?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                CompiledPlanStep step = compiled.Steps[i];
                string branch = i == count - 1 ? "└" : "├";
                text.Append('\n').Append("  ").Append(branch).Append("─ ")
                    .Append((i + 1).ToString("00")).Append("  ")
                    .Append(Action(step.Source));
                if (!string.IsNullOrEmpty(step.ExpectedStateHash))
                {
                    text.Append("  => ").Append(ShortHash(step.ExpectedStateHash));
                }
            }
            text.Append('\n').Append("  ").Append("   ").Append(count).Append(" step")
                .Append(count == 1 ? string.Empty : "s").Append(" verified");
            Write(LogLevel.Message, text);
        }

        internal static void Execute(int turn, int plan, int position, int total, TurnPlanStep step)
        {
            Write(LogLevel.Info,
                $"{Prefix} -> ACT    turn={turn}  plan={plan}  [{position}/{total}]  {Action(step)}");
        }

        internal static void StepPassed(string stepId, string stateHash)
        {
            Write(LogLevel.Info,
                $"{Prefix} +  OK     {Display(stepId, "-")}  state={ShortHash(stateHash)}");
        }

        internal static void TurnEnded(int turn, int plan, string stepId)
        {
            Write(LogLevel.Message,
                $"{Prefix} └─ TURN END  turn={turn}  plan={plan}  step={Display(stepId, "-")}");
        }

        internal static void Replan(int turn, int plan, string stepId, string reason, string expected = null, string actual = null)
        {
            StringBuilder text = Begin("REPLAN", $"turn {turn} / plan {plan}");
            Line(text, "AFTER", Display(stepId, "-"));
            Line(text, "CAUSE", Clean(reason, 220));
            if (expected != null || actual != null)
            {
                Line(text, "STATE", ShortHash(expected) + " -> " + ShortHash(actual));
            }
            End(text, "discarding remaining planned steps");
            Write(LogLevel.Warning, text);
        }

        internal static void Lethal(int turn, int patterns, long elapsedMs, int steps, string stateHash)
        {
            StringBuilder text = Begin("LOCAL LETHAL", "verified");
            Line(text, "TURN", turn.ToString());
            Line(text, "SEARCH", patterns + " patterns  " + elapsedMs + "ms");
            Line(text, "STATE", ShortHash(stateHash));
            End(text, steps + " steps; model bypassed");
            Write(LogLevel.Message, text);
        }

        internal static void Fallback(int turn, int plan, string stepId, string reason)
        {
            StringBuilder text = Begin("ORIGINAL AI", "control returned");
            Line(text, "TURN", turn.ToString());
            Line(text, "PLAN", plan + "  step=" + Display(stepId, "-"));
            End(text, Clean(reason, 260));
            Write(LogLevel.Error, text);
        }

        internal static void Warning(string area, string message)
        {
            Write(LogLevel.Warning, $"{Prefix} !  {area.ToUpperInvariant(),-10} {Clean(message, 300)}");
        }

        internal static void Error(string area, string message)
        {
            Write(LogLevel.Error, $"{Prefix} x  {area.ToUpperInvariant(),-10} {Clean(message, 300)}");
        }

        internal static void Debug(string area, string message)
        {
            Write(LogLevel.Debug, $"{Prefix} .  {area.ToUpperInvariant(),-10} {message}");
        }

        internal static string ShortHash(string hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return "-";
            }
            return hash.Length <= 12 ? hash : hash.Substring(0, 12);
        }

        private static string Action(TurnPlanStep step)
        {
            if (step == null)
            {
                return "UNKNOWN";
            }
            if (string.Equals(step.Type, "turn_end", StringComparison.Ordinal))
            {
                return "TURN END";
            }
            string type = (step.Type ?? "unknown").ToUpperInvariant();
            string mode = string.IsNullOrWhiteSpace(step.Mode) ? string.Empty : "/" + step.Mode.ToUpperInvariant();
            string targets = step.Targets == null || step.Targets.Count == 0
                ? string.Empty
                : " -> " + string.Join(", ", step.Targets);
            string boundary = step.ReplanAfter ? "  [REPLAN]" : string.Empty;
            return type + mode + "  " + Display(step.Actor, "-") + targets + boundary;
        }

        private static StringBuilder Begin(string title, string detail)
        {
            return new StringBuilder(Prefix)
                .Append(" ┌─ ").Append(title).Append("  [").Append(detail).Append(']');
        }

        private static void Line(StringBuilder text, string label, string value)
        {
            text.Append('\n').Append("  │  ").Append(label.PadRight(7)).Append(' ').Append(Display(value, "-"));
        }

        private static void End(StringBuilder text, string value)
        {
            text.Append('\n').Append("  └─ ").Append(Display(value, "-"));
        }

        private static string Display(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

        private static string Clean(string value, int maxLength)
        {
            value = Display(value, "-").Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        private static void Write(LogLevel level, object value)
        {
            Plugin.Logger.Log(level, value);
        }
    }
}
