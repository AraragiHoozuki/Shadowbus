using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Wizard;

namespace Shadowbus
{
    /// <summary>
    /// Writes the skill definition of every card to a reference CSV. The card
    /// master stores one skill as parallel comma separated columns, while
    /// BossRush abilities and enemy skills take the bracket form, so each row
    /// also carries the ready to paste bracket string.
    /// </summary>
    public static class CardSkillExporter
    {
        private static bool _exported;

        [HarmonyPatch(typeof(CardMaster), "CreateCardMaster")]
        [HarmonyPostfix]
        private static void CardMaster_CreateCardMaster_Postfix(CardMaster __result)
        {
            if (_exported || __result == null)
            {
                return;
            }

            try
            {
                Export(__result);
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[CardSkill] Card skill export failed: {exception.Message}");
            }
        }

        private static bool _namesExported;

        /// <summary>
        /// Card names are not localised yet when the card master is created, so
        /// the name lookup is written separately once the text masters are up.
        /// </summary>
        [HarmonyPatch(typeof(Master), nameof(Master.StartLoadAIIndividualData))]
        [HarmonyPostfix]
        private static void MasterLoaded_Postfix()
        {
            if (_namesExported)
            {
                return;
            }

            try
            {
                ExportNames();
            }
            catch (Exception exception)
            {
                Plugin.Logger.LogWarning($"[CardSkill] Card name export failed: {exception.Message}");
            }
        }

        private static void ExportNames()
        {
            CardMaster master = CardMaster.GetInstance(CardMaster.CardMasterId.Default);
            List<CardParameter> cards = master?.GetAllParameters()?
                .Where(card => card != null)
                .OrderBy(card => card.CardId)
                .ToList() ?? new List<CardParameter>();
            if (cards.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(PathHelper.CardMasterReferencePath);
            string path = Path.Combine(PathHelper.CardMasterReferencePath, "card_names.csv");

            StringBuilder csv = new StringBuilder();
            csv.AppendLine(
                "card_id,card_name,clan,char_type,cost,atk,life,base_card_id,skill_description,evo_skill_description");
            int named = 0;
            foreach (CardParameter card in cards)
            {
                string name = SafeText(() => card.CardName);
                if (!string.IsNullOrEmpty(name))
                {
                    named++;
                }
                csv.Append(card.CardId).Append(',')
                    .Append(Escape(name)).Append(',')
                    .Append(card.Clan).Append(',')
                    .Append(card.CharType).Append(',')
                    .Append(card.Cost).Append(',')
                    .Append(card.Atk).Append(',')
                    .Append(card.Life).Append(',')
                    .Append(card.BaseCardId).Append(',')
                    .Append(Escape(SafeText(() => card.SkillDescription))).Append(',')
                    // Cards whose only effect appears after evolving keep an empty
                    // SkillDescription and put their text here, so the WebEditor's
                    // card reference panel would otherwise show them with no text.
                    .Append(Escape(SafeText(() => card.EvoSkillDescription))).AppendLine();
            }

            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            _namesExported = true;
            Plugin.Logger.LogInfo(
                $"[CardSkill] Exported {cards.Count} card name row(s) ({named} named) to '{path}'.");
        }

        private static void Export(CardMaster master)
        {
            List<CardParameter> cards = master.GetAllParameters()?
                .Where(card => card != null)
                .OrderBy(card => card.CardId)
                .ToList() ?? new List<CardParameter>();
            if (cards.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(PathHelper.CardMasterReferencePath);
            string path = Path.Combine(PathHelper.CardMasterReferencePath, "card_skills.csv");

            StringBuilder csv = new StringBuilder();
            csv.AppendLine(string.Join(",", new[]
            {
                "card_id", "card_name", "clan", "char_type", "cost", "atk", "life", "evo_atk", "evo_life",
                "base_card_id", "normal_card_id", "foil_card_id", "skill_index", "is_field_count_consistent",
                "skill", "timing", "condition", "target", "option", "preprocess",
                "effect_path", "se_path", "effect_move_type", "engine_type", "effect_time", "effect_target_type",
                "bracket_skill", "skill_description"
            }));

            int rows = 0;
            int skillCards = 0;
            foreach (CardParameter card in cards)
            {
                List<string> skills = SplitSkillField(card.Skill);
                if (skills.Count == 0)
                {
                    continue;
                }

                List<string> timings = SplitSkillField(card.SkillTiming);
                List<string> conditions = SplitSkillField(card.SkillCondition);
                List<string> targets = SplitSkillField(card.SkillTarget);
                List<string> options = SplitSkillField(card.SkillOption);
                List<string> preprocesses = SplitSkillField(card.SkillPreprocess);
                bool consistent = new[] { timings, conditions, targets, options, preprocesses }
                    .All(list => list.Count == skills.Count);

                string description = SafeText(() => card.SkillDescription);
                string name = SafeText(() => card.CardName);
                skillCards++;

                for (int index = 0; index < skills.Count; index++)
                {
                    string skill = skills[index];
                    string timing = At(timings, index);
                    string condition = At(conditions, index);
                    string target = At(targets, index);
                    string option = At(options, index);
                    string preprocess = At(preprocesses, index);
                    string effectPath = At(card.SkillEffectPath, index);
                    string sePath = At(card.SkillSe, index);
                    string moveType = EnumAt(card.SkillMoveType, index);
                    string engineType = EnumAt(card.SkillEffectEnginType, index);
                    string effectTime = At(card.SkillEffectTime, index);
                    string effectTargetType = EnumAt(card.SkillEffectTargetType, index);

                    csv.AppendLine(string.Join(",", new[]
                    {
                        card.CardId.ToString(),
                        Escape(name),
                        card.Clan.ToString(),
                        card.CharType.ToString(),
                        card.Cost.ToString(),
                        card.Atk.ToString(),
                        card.Life.ToString(),
                        card.EvoAtk.ToString(),
                        card.EvoLife.ToString(),
                        card.BaseCardId.ToString(),
                        card.NormalCardId.ToString(),
                        card.FoilCardId.ToString(),
                        index.ToString(),
                        consistent ? "1" : "0",
                        Escape(skill),
                        Escape(timing),
                        Escape(condition),
                        Escape(target),
                        Escape(option),
                        Escape(preprocess),
                        Escape(effectPath),
                        Escape(sePath),
                        Escape(moveType),
                        Escape(engineType),
                        Escape(effectTime),
                        Escape(effectTargetType),
                        Escape(BuildBracketSkill(
                            skill, timing, condition, target, option, preprocess,
                            effectPath, sePath, moveType, engineType, effectTime, effectTargetType)),
                        Escape(description)
                    }));
                    rows++;
                }
            }

            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            _exported = true;
            Plugin.Logger.LogInfo(
                $"[CardSkill] Exported {rows} skill row(s) from {skillCards} card(s) of {cards.Count} to '{path}'.");
        }

        /// <summary>
        /// Composes the bracket form BossRush `skill` and `enemy_skill` accept.
        /// Presentation segments are only appended when the card actually has
        /// them, matching how the original BossRush strings are written.
        /// </summary>
        private static string BuildBracketSkill(
            string skill,
            string timing,
            string condition,
            string target,
            string option,
            string preprocess,
            string effectPath,
            string sePath,
            string moveType,
            string engineType,
            string effectTime,
            string effectTargetType)
        {
            StringBuilder text = new StringBuilder();
            text.Append("(skill:").Append(Or(skill, "none")).Append(')');
            text.Append("(timing:").Append(Or(timing, "none")).Append(')');
            text.Append("(condition:").Append(Or(condition, "none")).Append(')');
            text.Append("(target:").Append(Or(target, "none")).Append(')');
            text.Append("(option:").Append(Or(option, "none")).Append(')');
            text.Append("(preprocess:").Append(Or(preprocess, "none")).Append(')');
            AppendOptional(text, "effect_path", effectPath);
            AppendOptional(text, "se_path", sePath);
            AppendOptional(text, "effect_move_type", moveType);
            AppendOptional(text, "engine_type", engineType);
            AppendOptional(text, "effect_time", effectTime);
            AppendOptional(text, "effect_target_type", effectTargetType);
            return text.ToString();
        }

        private static void AppendOptional(StringBuilder text, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(value, "NONE", StringComparison.Ordinal))
            {
                text.Append('(').Append(key).Append(':').Append(value).Append(')');
            }
        }

        /// <summary>
        /// Splits on the commas that separate skills. Commas inside parentheses
        /// belong to one preprocess or option argument list, for example
        /// `remove_after_action=(count=1,turn=2)`, and must be kept.
        /// </summary>
        private static List<string> SplitSkillField(string value)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(value))
            {
                return parts;
            }

            int depth = 0;
            StringBuilder current = new StringBuilder();
            foreach (char character in value)
            {
                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth = Math.Max(0, depth - 1);
                }

                if (character == ',' && depth == 0)
                {
                    parts.Add(current.ToString());
                    current.Length = 0;
                    continue;
                }
                current.Append(character);
            }
            parts.Add(current.ToString());
            return parts;
        }

        private static string At(List<string> values, int index)
        {
            return values != null && index >= 0 && index < values.Count ? values[index] : string.Empty;
        }

        private static string At(string[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index] ?? string.Empty : string.Empty;
        }

        private static string EnumAt<T>(T[] values, int index)
        {
            return values != null && index >= 0 && index < values.Length ? values[index].ToString() : string.Empty;
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string SafeText(Func<string> read)
        {
            try
            {
                return read() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string Escape(string value)
        {
            string text = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            if (text.IndexOfAny(new[] { ',', '"' }) < 0)
            {
                return text;
            }
            return '"' + text.Replace("\"", "\"\"") + '"';
        }
    }
}
