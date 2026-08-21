#!/usr/bin/env node
// Regenerates src/data/cardReference.generated.ts: the skill fields and effect
// text of every game card, for the floating card reference panel.
//
// Two exports are joined, because neither alone is enough:
//
//   * CardMaster_Default_backup.csv — the card master table as the game ships
//     it, before any CardMaster patch applies. This is the only source that
//     keeps the six skill fields verbatim, including the `//` that separates the
//     pre-evolution list from the post-evolution one, plus the normal and
//     evolution presentation columns. It has no text: names and descriptions are
//     localisation ids there (CN_930844060, SD_930844060).
//   * Mods/CardMaster/Reference/card_names.csv — written by CardSkillExporter
//     after the text masters load, so this is where the localised effect text
//     lives. Its language is whatever the exporting machine ran.
//
// Neither CSV is committed (3.3 MB and ~20 MB), so regenerate after a game
// update:
//
//   npm run build:reference
//   npm run build:reference -- <master csv> <names csv>
//
// Foil rows (id % 10 === 1) duplicate their base card and are dropped, matching
// build-card-catalog.mjs and the runtime's foil arithmetic.

import { writeFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { fail as failWith, field, integer as parseInteger, kilobytes, readCsv } from "./lib/csv.mjs";

const SCRIPT = "build-card-reference";
const fail = (message) => failWith(SCRIPT, message);
const integer = (text, label, lineNumber) => parseInteger(SCRIPT, text, label, lineNumber);

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const webEditorRoot = resolve(scriptDirectory, "..");
const outputPath = resolve(webEditorRoot, "src/data/cardReference.generated.ts");
const gameRoot = resolve(webEditorRoot, "../../../../SteamLibrary/steamapps/common/Shadowverse");
const defaultMasterPath = resolve(gameRoot, "CardMaster_Default_backup.csv");
const defaultNamesPath = resolve(gameRoot, "Mods/CardMaster/Reference/card_names.csv");

/** Kept in sync with src/data/cards.ts; the master dump predates user patches so it should hold none. */
const CUSTOM_CARD_ID_MIN = 999990000;

/** Field separator inside the blob. Verified absent from both exports; a collision is a hard error. */
const SEPARATOR = "~";

/**
 * Blob column order. `cardReference.generated.ts` re-exports this list and
 * src/data/cardReference.ts checks it against its own decoder, so reordering
 * here without updating the decoder fails at load instead of silently shifting
 * every field by one.
 *
 * Trailing empty columns are trimmed per line, which is why the frequently
 * empty presentation columns come last: most cards then stop after `preprocess`.
 */
const COLUMNS = [
  "idDelta",
  "skill", "timing", "condition", "target", "option", "preprocess",
  "description", "evoDescription", "effectCondition",
  "effectPath", "sePath", "moveType", "engineType", "effectTime", "targetType",
  "evoEffectPath", "evoSePath", "evoMoveType", "evoEngineType", "evoEffectTime",
];

/** Columns up to and including `preprocess`; a shorter line cannot be a record. */
const REQUIRED_COLUMNS = 7;

/** Blob column -> master CSV column. `idDelta`, description and evoDescription are filled separately. */
const MASTER_SOURCE = {
  skill: "skill",
  timing: "skill_timing",
  condition: "skill_condition",
  target: "skill_target",
  option: "skill_option",
  preprocess: "skill_preprocess",
  effectCondition: "skill_effect_condition",
  effectPath: "skill_effect_path",
  sePath: "skill_se",
  moveType: "skill_move_type",
  engineType: "skill_effect_engin_type",
  effectTime: "skill_effect_time",
  targetType: "skill_effect_target_type",
  evoEffectPath: "evo_skill_effect_path",
  evoSePath: "evo_skill_se",
  evoMoveType: "evo_skill_move_type",
  evoEngineType: "evo_skill_effect_engin_type",
  evoEffectTime: "evo_skill_effect_time",
};

/** Presentation columns, reported separately so their share of the blob is visible. */
const PRESENTATION_COLUMNS = COLUMNS.filter((name) => /^(?:evo)?(?:effectPath|sePath|moveType|engineType|effectTime|targetType)$/i.test(name));

/**
 * Normal presentation column -> its evolution twin. `targetType` is deliberately
 * absent: skill_effect_target_type is the one column with no `evo_` twin in the
 * dump, so it carries both forms in a single field split by `//` and the game
 * exposes the second half as CardParameter.EvoSkillEffectTargetType.
 *
 * A twinned column can still carry `//` — card 820531010 writes `2.5,0,0//0` in
 * skill_effect_time — but there the second half only repeats what the twin already
 * says, so src/models/cardDsl.ts takes the pre-`//` half for the normal form and
 * reads the evolution form from the twin. A second half that disagreed with its
 * twin would break that, which is what the check below looks for.
 */
const EVOLUTION_TWINS = {
  effectPath: "evoEffectPath",
  sePath: "evoSePath",
  moveType: "evoMoveType",
  engineType: "evoEngineType",
  effectTime: "evoEffectTime",
};

const masterPath = process.argv[2] ? resolve(process.argv[2]) : defaultMasterPath;
const namesPath = process.argv[3] ? resolve(process.argv[3]) : defaultNamesPath;

const master = readCsv(SCRIPT, masterPath, ["card_id", "is_foil", "rarity", "TribeNameId", ...Object.values(MASTER_SOURCE)]);
const names = readCsv(SCRIPT, namesPath, ["card_id", "skill_description"]);

// The dump was written by a tool that read the game's UTF-8 through a DBCS
// codepage, so Japanese TribeNameId values (TN_指揮官, TN_レヴィオン …) came out
// mangled. Where the mangled character formed an invalid byte pair with the comma
// ending the field, both were replaced by one `?`: the row loses a boundary, every
// later column sits one position early, and the line is padded with a trailing
// empty. 251 of 5933 rows here, so reading `skill` out of `rarity` would quietly
// wreck 4% of the panel. `rarity` is the first integer column past the damage, so
// it detects the shift; restoring the boundary after the final `?` repairs it.
// Rows with several tribes quote the field ("TN_兵士,TN_レヴィオン") and lost the
// closing quote instead, which cascades — those are dropped by id.
const width = master.header.length;
const rarityIndex = master.columns.get("rarity");
const tribeIndex = master.columns.get("TribeNameId");
const isShifted = (row) => !/^\d+$/.test((row[rarityIndex] ?? "").trim());

function repairShiftedRow(row) {
  if (row.length !== width || row.at(-1) !== "") return null;
  const damaged = row[tribeIndex];
  const boundary = damaged.lastIndexOf("?");
  const recovered = boundary < 0 ? "" : damaged.slice(boundary + 1);
  if (!recovered) return null;
  const repaired = [...row.slice(0, tribeIndex), damaged.slice(0, boundary + 1), recovered, ...row.slice(tribeIndex + 1, width - 1)];
  // Accept only when the repair restores the invariant it was detected by, so a
  // differently damaged dump fails loudly instead of shifting.
  return isShifted(repaired) ? null : repaired;
}

// CardSkillExporter only learned to write evo_skill_description later; a CSV from
// before that still works, it just has no text for evolution only effects.
const hasEvoDescription = names.columns.has("evo_skill_description");

const text = new Map();
for (let index = 0; index < names.rows.length; index++) {
  const row = names.rows[index];
  const id = integer(field(names.columns, row, "card_id"), "card_id", index + 2);
  text.set(id, {
    description: field(names.columns, row, "skill_description"),
    evoDescription: hasEvoDescription ? field(names.columns, row, "evo_skill_description") : "",
  });
}

/** True when the field holds nothing but `none` entries in both halves. */
function isEmptySkill(value) {
  if (!value) return true;
  return value.split("//").join(",").split(",").every((entry) => {
    const trimmed = entry.trim();
    return !trimmed || trimmed === "none";
  });
}

const allIds = new Set();
const parsed = [];
const unrepairable = [];
let repairedRows = 0;
for (let index = 0; index < master.rows.length; index++) {
  const lineNumber = index + 2;
  let row = master.rows[index];
  const id = integer(field(master.columns, row, "card_id"), "card_id", lineNumber);
  // Added before the shift check so dropping a row cannot orphan its foil twin.
  allIds.add(id);
  if (isShifted(row)) {
    const repaired = repairShiftedRow(row);
    if (!repaired) { unrepairable.push(id); continue; }
    row = repaired;
    repairedRows++;
  }
  const record = { id, lineNumber, isFoil: field(master.columns, row, "is_foil") === "1" };
  for (const [column, source] of Object.entries(MASTER_SOURCE)) record[column] = field(master.columns, row, source);
  parsed.push(record);
}

// If the rule explains less of the damage than it fails to, the dump is broken in
// some way this script does not know about and guessing would be worse than stopping.
if (unrepairable.length > repairedRows) {
  fail(`${unrepairable.length} 行列错位无法修复，只修好了 ${repairedRows} 行，说明破坏方式和脚本假设的不同。示例：${unrepairable.slice(0, 5).join(", ")}`);
}

const foils = parsed.filter((card) => card.id % 10 === 1);
const orphanFoils = foils.filter((card) => !allIds.has(card.id - 1));
if (orphanFoils.length) {
  fail(`${orphanFoils.length} 个闪卡行没有对应的基础卡，丢弃会丢数据。示例：${orphanFoils.slice(0, 5).map((card) => card.id).join(", ")}`);
}
const mismatchedFoilFlags = parsed.filter((card) => card.isFoil !== (card.id % 10 === 1));
if (mismatchedFoilFlags.length) {
  fail(`${mismatchedFoilFlags.length} 行的 is_foil 与 id 末位不一致，闪卡判定不再可靠。示例：${mismatchedFoilFlags.slice(0, 5).map((card) => card.id).join(", ")}`);
}

const custom = parsed.filter((card) => card.id >= CUSTOM_CARD_ID_MIN);
const base = parsed
  .filter((card) => card.id % 10 !== 1 && card.id < CUSTOM_CARD_ID_MIN)
  .sort((left, right) => left.id - right.id);
if (!base.length) fail("过滤后没有剩下任何卡牌。");

let missingText = 0;
for (const card of base) {
  const found = text.get(card.id);
  if (!found) missingText++;
  card.description = found?.description ?? "";
  card.evoDescription = found?.evoDescription ?? "";
}

// A card with no skills and no text has nothing to show; the always loaded
// catalog still resolves its name, so the panel can say so without this row.
const kept = base.filter((card) => !isEmptySkill(card.skill) || card.description || card.evoDescription || card.effectCondition);

const halvedNormalColumns = Object.keys(EVOLUTION_TWINS).filter((column) => base.some((card) => card[column].includes("//")));
const twinConflicts = base.filter((card) => Object.entries(EVOLUTION_TWINS).some(([column, twin]) => {
  const boundary = card[column].indexOf("//");
  return boundary >= 0 && card[column].slice(boundary + 2) !== card[twin];
}));

const lines = [];
let previousId = 0;
let presentationBytes = 0;
for (const card of kept) {
  const values = COLUMNS.map((column) => column === "idDelta" ? String(card.id - previousId) : card[column] ?? "");
  for (let index = 0; index < values.length; index++) {
    const value = values[index];
    if (value.includes(SEPARATOR)) fail(`卡牌 ${card.id} 的 ${COLUMNS[index]} 含有分隔符 ${SEPARATOR}，需要改用其他分隔符：${JSON.stringify(value)}`);
    if (/[\r\n]/.test(value)) fail(`卡牌 ${card.id} 的 ${COLUMNS[index]} 含有换行，导出应当已经把换行替换为空格：${JSON.stringify(value)}`);
  }
  while (values.length > REQUIRED_COLUMNS && values.at(-1) === "") values.pop();
  presentationBytes += PRESENTATION_COLUMNS.reduce((total, column) => total + Buffer.byteLength(card[column], "utf8"), 0);
  lines.push(values.join(SEPARATOR));
  previousId = card.id;
}

const blob = lines.join("\n");
/** `${` starts an interpolation and backticks close the literal, so both are escaped. */
const escaped = blob.replace(/[\\`]/g, "\\$&").replace(/\$\{/g, "\\${");

const generated = `// Generated by scripts/build-card-reference.mjs from the game's card master dump
// and card_names.csv export. Do not edit by hand: run \`npm run build:reference\`.
//
// ${master.rows.length} master rows -> ${base.length} base cards -> ${kept.length} with skills or effect text
// (dropped ${foils.length} foil duplicates${custom.length ? `, ${custom.length} user created card(s)` : ""}, ${base.length - kept.length} cards with neither).
//
// One line per card, fields separated by "${SEPARATOR}", trailing empty fields
// trimmed. idDelta accumulates from 0, so line one carries an absolute ID. The
// six skill fields keep the game's own text verbatim, including the "//" that
// separates the pre-evolution list from the post-evolution one.
${hasEvoDescription ? "" : `//
// NOTE: this was built from a card_names.csv without an evo_skill_description
// column, so cards whose effect is evolution only have no text. Launch the game
// once with the current build to refresh the export, then rerun this script.
`}
/** Blob column order; src/data/cardReference.ts checks this against its decoder. */
export const cardReferenceColumns = ${JSON.stringify(COLUMNS)} as const;

/** Records in the blob, so a decode can be checked without walking it twice. */
export const cardReferenceCount = ${kept.length};

/** True when the source export carried evolution effect text. */
export const cardReferenceHasEvoText = ${hasEvoDescription};

export const cardReferenceBlob = \`${escaped}\`;
`;

writeFileSync(outputPath, generated, "utf8");

console.log(`${SCRIPT}: 读取 ${relative(process.cwd(), masterPath) || masterPath}`);
console.log(`         读取 ${relative(process.cwd(), namesPath) || namesPath}${hasEvoDescription ? "（含进化效果文）" : "（无 evo_skill_description 列，进化专属效果缺少文本）"}`);
console.log(`  ${master.rows.length} 行 -> ${base.length} 张基础卡 -> ${kept.length} 张有技能或效果文（丢弃 ${foils.length} 个闪卡副本${custom.length ? `、${custom.length} 张自制卡` : ""}、${base.length - kept.length} 张无内容）`);
if (repairedRows) console.log(`  修复 ${repairedRows} 行被编码破坏的列错位（TribeNameId 的乱码字符吞掉了字段分隔符）`);
if (unrepairable.length) console.log(`  跳过 ${unrepairable.length} 行无法修复的列错位（多部族的 TribeNameId 被引号包裹，丢失的是引号）：${unrepairable.join(" ")}`);
if (missingText) console.log(`  ${missingText} 张卡在 card_names.csv 中没有对应行，效果文留空`);
console.log(`  ${kept.filter((card) => card.skill.includes("//")).length} 张卡的技能字段含 //，${kept.filter((card) => card.description || card.evoDescription).length} 张有效果文`);
console.log(`  ${kept.filter((card) => card.targetType.includes("//")).length} 张卡的 skill_effect_target_type 含 //（进化后的目标类型，没有独立的 evo_ 列）`);
if (halvedNormalColumns.length) {
  console.log(`  ${halvedNormalColumns.join("、")} 也有卡片含 //，取前半段给进化前形态，进化后仍读 evo_ 列`);
}
if (twinConflicts.length) {
  console.log(`  警告：${twinConflicts.length} 张卡的演出字段后半段与对应 evo_ 列不一致，进化形态的演出可能不对。示例：${twinConflicts.slice(0, 5).map((card) => card.id).join(" ")}`);
}
console.log(`  写入 ${relative(process.cwd(), outputPath)}（blob ${kilobytes(blob)}，其中演出字段 ${(presentationBytes / 1024).toFixed(1)} KB，文件 ${kilobytes(generated)}）`);
