import type { CardReferenceEntry } from "../data/cardReference";
import { formatSkillDsl, parseSkillDsl, type SkillDslGroup } from "./skillDsl";
import { emptySkillRow, parseSkillGroups, EVOLUTION_SEPARATOR, type SkillRow } from "./skills";

/**
 * Turns a game card's skill fields into the bracket DSL the editor uses
 * elsewhere, so an existing card can be read as a worked example.
 *
 * Two things the DSL cannot express, and this module therefore keeps apart:
 *
 *   * The `//` split. `(skill:...)` groups are a flat list, so a card with
 *     evolution skills yields two separate DSL strings — one per form — rather
 *     than one string that silently drops half the card.
 *   * `skill_effect_condition`. The six field model has no slot for it, so it is
 *     carried on the entry and shown, not folded into the DSL.
 */

/** One skill of one form, with the presentation columns that belong to it. */
export interface CardSkillGroup {
  form: "normal" | "evolved";
  /** Position within its own form, 1 based, matching the comma slot in the fields. */
  index: number;
  skill: string;
  timing: string;
  condition: string;
  target: string;
  option: string;
  preprocess: string;
  effectPath: string;
  sePath: string;
  moveType: string;
  engineType: string;
  effectTime: string;
  /** From `skill_effect_target_type`, whose two `//` halves cover both forms. */
  targetType: string;
}

export interface CardSkillBreakdown {
  /** The six parallel fields verbatim, `//` included. */
  fields: SkillRow;
  normal: CardSkillGroup[];
  evolved: CardSkillGroup[];
  /** The fields carried a `//`, so the card distinguishes the two forms at all. */
  hasEvolution: boolean;
}

/** The six CardMaster field names, in the order a patch lists them. */
export function cardMasterSkillFields(entry: CardReferenceEntry): SkillRow {
  return {
    Skill: entry.skill,
    SkillTiming: entry.timing,
    SkillCondition: entry.condition,
    SkillTarget: entry.target,
    SkillOption: entry.option,
    SkillPreprocess: entry.preprocess,
  };
}

/** Splits a presentation column into its comma slots; a missing slot reads as empty. */
const slots = (value: string) => value ? value.split(",") : [];

/**
 * Halves a presentation column at its first `//`, mirroring the six skill fields.
 *
 * `skill_effect_target_type` is the one column with no `evo_` twin in the card
 * master: it carries both forms in a single field and the game's CardParameter
 * exposes the second half as `EvoSkillEffectTargetType`, so both halves are used.
 * Card 100621020 is the worked example — `none//single,single` for its one normal
 * and two evolution skills. A twinned column can carry `//` too (820531010 writes
 * `2.5,0,0//0` in skill_effect_time), but there the second half only repeats what
 * the twin already says, so only the pre-`//` half is taken and the evolution form
 * keeps reading its own column.
 */
function columnHalves(value: string) {
  const separator = value.indexOf(EVOLUTION_SEPARATOR);
  if (separator < 0) return { normal: slots(value), evolved: [] as string[] };
  return {
    normal: slots(value.slice(0, separator)),
    evolved: slots(value.slice(separator + EVOLUTION_SEPARATOR.length)),
  };
}

export function cardSkillBreakdown(entry: CardReferenceEntry): CardSkillBreakdown {
  const fields = cardMasterSkillFields(entry);
  const groups = parseSkillGroups(fields, "change");
  const targetType = columnHalves(entry.targetType);

  // Normal presentation arrays size to the pre-evolution half and the evo ones to
  // the post-evolution half, so each form indexes its own set from zero.
  const presentation = {
    normal: {
      effectPath: columnHalves(entry.effectPath).normal,
      sePath: columnHalves(entry.sePath).normal,
      moveType: columnHalves(entry.moveType).normal,
      engineType: columnHalves(entry.engineType).normal,
      effectTime: columnHalves(entry.effectTime).normal,
      targetType: targetType.normal,
    },
    evolved: {
      // The evo_ columns hold one form each, so they are never halved.
      effectPath: slots(entry.evoEffectPath),
      sePath: slots(entry.evoSePath),
      moveType: slots(entry.evoMoveType),
      engineType: slots(entry.evoEngineType),
      effectTime: slots(entry.evoEffectTime),
      targetType: targetType.evolved,
    },
  };

  const build = (rows: readonly SkillRow[], form: "normal" | "evolved"): CardSkillGroup[] => {
    const columns = presentation[form];
    return rows.map((row, index) => ({
      form,
      index: index + 1,
      skill: row.Skill.trim(),
      timing: row.SkillTiming.trim(),
      condition: row.SkillCondition.trim(),
      target: row.SkillTarget.trim(),
      option: row.SkillOption.trim(),
      preprocess: row.SkillPreprocess.trim(),
      effectPath: (columns.effectPath[index] ?? "").trim(),
      sePath: (columns.sePath[index] ?? "").trim(),
      moveType: (columns.moveType[index] ?? "").trim(),
      engineType: (columns.engineType[index] ?? "").trim(),
      effectTime: (columns.effectTime[index] ?? "").trim(),
      targetType: (columns.targetType[index] ?? "").trim(),
    }));
  };

  return {
    fields,
    normal: build(groups.normal, "normal"),
    evolved: build(groups.evolved, "evolved"),
    hasEvolution: groups.hasEvolution,
  };
}

/** Mandatory DSL keys default to `none`, exactly as CardSkillExporter.BuildBracketSkill does. */
const or = (value: string) => value || "none";

/** Presentation keys are omitted when unset, so the DSL stays as short as the card allows. */
const meaningful = (value: string) => Boolean(value) && value.toLowerCase() !== "none";

/**
 * Blocks for one skill, in BuildBracketSkill's order: the six mandatory keys and
 * then whichever presentation keys the card actually sets.
 */
export function cardSkillDslGroup(group: CardSkillGroup): SkillDslGroup {
  const blocks = [
    { key: "skill", value: or(group.skill) },
    { key: "timing", value: or(group.timing) },
    { key: "condition", value: or(group.condition) },
    { key: "target", value: or(group.target) },
    { key: "option", value: or(group.option) },
    { key: "preprocess", value: or(group.preprocess) },
  ];
  const optional: [string, string][] = [
    ["effect_path", group.effectPath],
    ["se_path", group.sePath],
    ["effect_move_type", group.moveType],
    ["engine_type", group.engineType],
    ["effect_time", group.effectTime],
    ["effect_target_type", group.targetType],
  ];
  for (const [key, value] of optional) if (meaningful(value)) blocks.push({ key, value });
  return { blocks };
}

/** The DSL for one form's skills, comma joined the way `enemy_skill` expects. */
export function cardSkillDsl(groups: readonly CardSkillGroup[]) {
  return formatSkillDsl(groups.map(cardSkillDslGroup));
}

/**
 * The six fields as a `stringChangeFields` snippet, ready to paste into a
 * CardMaster patch. This is the only copy that keeps the `//`, so it is what to
 * use when the card's evolution skills matter.
 */
export function cardMasterFieldsSnippet(entry: CardReferenceEntry) {
  return JSON.stringify({ stringChangeFields: cardMasterSkillFields(entry) }, null, 2);
}

/** Which of the six fields each DSL key fills; anything else has no slot here. */
const dslFieldKeys: Record<string, keyof SkillRow> = {
  skill: "Skill",
  timing: "SkillTiming",
  condition: "SkillCondition",
  target: "SkillTarget",
  option: "SkillOption",
  preprocess: "SkillPreprocess",
};

export interface SkillDslImport {
  /** One row per DSL group, ready to append to a form's list. */
  rows: SkillRow[];
  /** Keys the six field model has no slot for, in the order they appeared. */
  ignored: string[];
  /** Set when nothing can be imported; `rows` is then empty. */
  error?: string;
}

/**
 * Reads bracket DSL back into rows of the six parallel fields, the inverse of
 * `cardSkillDsl`, so a DSL copied out of the reference panel can be appended to a
 * patch.
 *
 * The DSL is the wider language of the two, and this is where the difference
 * bites. A DSL value may contain a comma — `(condition:count_over(me.hand,3))`
 * parses as one balanced value — but the six fields are comma separated lists, so
 * writing that value in would split it into two entries and desync all six
 * fields. The game keeps such expressions in `skill_effect_condition`, a field
 * this editor does not model, so a comma is refused rather than silently
 * corrupting the alignment. `//` is refused for the same reason: it would invent
 * an evolution split inside one entry.
 *
 * Presentation keys (`effect_path` and friends) are reported in `ignored` instead
 * of failing: they are a normal part of a copied DSL, and the six fields simply
 * do not hold them.
 */
export function skillRowsFromDsl(input: string): SkillDslImport {
  if (!input.trim()) return { rows: [], ignored: [] };
  const parsed = parseSkillDsl(input);
  if (parsed.error) return { rows: [], ignored: [], error: parsed.error };

  const rows: SkillRow[] = [];
  const ignored: string[] = [];
  for (const [index, group] of parsed.groups.entries()) {
    const row = emptySkillRow();
    let recognised = 0;
    for (const block of group.blocks) {
      const field = dslFieldKeys[block.key.toLowerCase()];
      if (!field) {
        if (!ignored.includes(block.key)) ignored.push(block.key);
        continue;
      }
      if (block.value.includes(",")) {
        return { rows: [], ignored: [], error: `第 ${index + 1} 组的 ${block.key} 含逗号（${block.value}）。六个字段用逗号分隔条目，这个值会被拆成两条并让字段错位；这类表达式属于 skill_effect_condition，本编辑器不管理。` };
      }
      if (block.value.includes(EVOLUTION_SEPARATOR)) {
        return { rows: [], ignored: [], error: `第 ${index + 1} 组的 ${block.key} 含 ${EVOLUTION_SEPARATOR}。请把两个形态的 DSL 分别导入到普通形态和进化形态。` };
      }
      row[field] = block.value;
      recognised++;
    }
    if (!recognised) return { rows: [], ignored: [], error: `第 ${index + 1} 组没有任何可识别的技能字段，${Object.keys(dslFieldKeys).join(" / ")} 至少要有一个。` };
    rows.push(row);
  }
  return { rows, ignored };
}
