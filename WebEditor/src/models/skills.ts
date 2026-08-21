import { skillParallelKeys } from "../data/catalog";

/**
 * A card's six parallel skill fields hold two lists, not one:
 *
 *   字段 = 进化前列表 [ "//" 进化后列表 ]
 *   列表 = 条目 ("," 条目)*
 *
 * `none//damage,heal` therefore means "nothing before evolving; deal damage and
 * heal after evolving" — one normal entry and two evolution entries, not two
 * comma slots where the first carries a variant. The game keeps the halves in
 * separate collections (NormalSkills / EvolutionSkills) and sizes the normal
 * presentation arrays (SkillEffectPath and friends) to the first half only,
 * while EvoSkillEffectPath sizes to the second.
 */
export const EVOLUTION_SEPARATOR = "//";

/**
 * One entry of the six parallel fields. A type alias rather than an interface so
 * that it stays assignable to the `Record<string, string>` the readers below
 * take: TypeScript only infers an index signature for aliases.
 */
export type SkillRow = {
  Skill: string;
  SkillTiming: string;
  SkillCondition: string;
  SkillTarget: string;
  SkillOption: string;
  SkillPreprocess: string;
};

/** Which map of a CardMaster patch owns the six skill fields. */
export type SkillFieldSource = "append" | "change";

export interface SkillGroups {
  normal: SkillRow[];
  evolved: SkillRow[];
  /** The source carried a `//`, so serialising keeps the half even while it is empty. */
  hasEvolution: boolean;
  /** Append mode only: every field began with `,` to extend the template's own skills. */
  leadingComma: boolean;
}

export const emptySkillRow = (): SkillRow => ({
  Skill: "",
  SkillTiming: "",
  SkillCondition: "none",
  SkillTarget: "none",
  SkillOption: "none",
  SkillPreprocess: "none",
});

const skillKeys = skillParallelKeys as readonly (keyof SkillRow)[];

export const isSkillField = (key: string) => (skillKeys as readonly string[]).includes(key);

/** Drops the six skill fields, keeping every other entry of the map untouched. */
export const withoutSkillFields = (fields: Record<string, string>) =>
  Object.fromEntries(Object.entries(fields).filter(([key]) => !isSkillField(key)));

/** Keeps only the six skill fields that the map actually has. */
export const onlySkillFields = (fields: Record<string, string>) =>
  Object.fromEntries(skillKeys.filter((key) => key in fields).map((key) => [key, fields[key]]));

function rowsFrom(values: Record<keyof SkillRow, string[]>): SkillRow[] {
  const count = Math.max(0, ...skillKeys.map((key) => values[key].length));
  const rows: SkillRow[] = [];
  for (let index = 0; index < count; index++) {
    rows.push(Object.fromEntries(skillKeys.map((key) => [key, values[key][index] ?? ""])) as unknown as SkillRow);
  }
  // A lone all-empty row is what an empty field splits into, not a real skill.
  if (rows.length === 1 && skillKeys.every((key) => rows[0][key] === "")) return [];
  return rows;
}

/**
 * Splits one field at its first `//`. Only the first counts as the boundary, so
 * a malformed field with several keeps the remainder verbatim in the evolution
 * half instead of losing it.
 */
function splitHalves(raw: string) {
  const boundary = raw.indexOf(EVOLUTION_SEPARATOR);
  if (boundary < 0) return { normal: raw, evolved: "", split: false };
  return { normal: raw.slice(0, boundary), evolved: raw.slice(boundary + EVOLUTION_SEPARATOR.length), split: true };
}

export function parseSkillGroups(fields: Record<string, string>, source: SkillFieldSource): SkillGroups {
  const populated = skillKeys.filter((key) => fields[key] != null);
  // Replacement writes the field whole, so a leading comma there is a real empty entry.
  const leadingComma = source === "append" && populated.length > 0 && populated.every((key) => fields[key].startsWith(","));
  const halves = Object.fromEntries(skillKeys.map((key) => {
    const raw = fields[key] ?? "";
    return [key, splitHalves(leadingComma ? raw.slice(1) : raw)];
  })) as Record<keyof SkillRow, ReturnType<typeof splitHalves>>;

  return {
    normal: rowsFrom(Object.fromEntries(skillKeys.map((key) => [key, halves[key].normal.split(",")])) as Record<keyof SkillRow, string[]>),
    // A field without `//` contributes no evolution entries at all, rather than one empty one.
    evolved: rowsFrom(Object.fromEntries(skillKeys.map((key) => [key, halves[key].split ? halves[key].evolved.split(",") : []])) as Record<keyof SkillRow, string[]>),
    hasEvolution: populated.some((key) => fields[key].includes(EVOLUTION_SEPARATOR)),
    leadingComma,
  };
}

export function applySkillGroups(fields: Record<string, string>, groups: SkillGroups): Record<string, string> {
  const rest = withoutSkillFields(fields);
  const keepEvolution = groups.evolved.length > 0 || groups.hasEvolution;
  if (!groups.normal.length && !keepEvolution) return rest;
  for (const key of skillKeys) {
    const normal = groups.normal.map((row) => row[key]).join(",");
    const evolved = groups.evolved.map((row) => row[key]).join(",");
    rest[key] = `${groups.leadingComma ? "," : ""}${normal}${keepEvolution ? `${EVOLUTION_SEPARATOR}${evolved}` : ""}`;
  }
  return rest;
}

export interface SkillFieldShape {
  key: string;
  /** How many `//` the field contains; more than one is always malformed. */
  separators: number;
  normal: number;
  /** Entry count after the `//`, or null when the field has none. */
  evolved: number | null;
}

/** Per-field entry counts, for checking that the six fields stay parallel in both halves. */
export function skillFieldShapes(fields: Record<string, string>, source: SkillFieldSource): SkillFieldShape[] {
  const populated = skillKeys.filter((key) => fields[key] != null);
  const leadingComma = source === "append" && populated.length > 0 && populated.every((key) => fields[key].startsWith(","));
  return populated.map((key) => {
    const raw = fields[key];
    const body = leadingComma ? raw.slice(1) : raw;
    const halves = splitHalves(body);
    return {
      key,
      separators: body.split(EVOLUTION_SEPARATOR).length - 1,
      normal: halves.normal.split(",").length,
      evolved: halves.split ? halves.evolved.split(",").length : null,
    };
  });
}
