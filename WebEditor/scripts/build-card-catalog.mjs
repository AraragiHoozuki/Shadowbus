#!/usr/bin/env node
// Regenerates src/data/cards.generated.ts from the game's card_names.csv export.
//
// The CSV is not committed: it is ~3.3 MB, and CardSkillExporter writes it into
// Mods/CardMaster/Reference/card_names.csv on the machine that runs the game, so
// its card pool and language are whatever that installation had. Point this
// script at such an export whenever the game ships new cards.
//
//   npm run build:catalog                    # use the default game path below
//   npm run build:catalog -- <csv path>
//
// Two filters run before anything is written, both verified against the real
// export:
//
//   * Foil rows (id % 10 === 1) duplicate their base card exactly. Dropping
//     them halves the table; the runtime maps a foil ID back with the same
//     arithmetic. The script fails if any foil lacks its base row, which would
//     make the drop lossy.
//   * IDs at or above 999990000 are cards injected by the exporting user's own
//     CardMaster patches, not game data, and must not ship to everyone.
//
// Card names patched onto existing cards do NOT leak: the exporter reads names
// before the localisation patches apply, so official names stay authentic.

import { readFileSync, writeFileSync } from "node:fs";
import { dirname, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = dirname(fileURLToPath(import.meta.url));
const webEditorRoot = resolve(scriptDirectory, "..");
const outputPath = resolve(webEditorRoot, "src/data/cards.generated.ts");
const defaultCsvPath = resolve(
  webEditorRoot,
  "../../../../SteamLibrary/steamapps/common/Shadowverse/Mods/CardMaster/Reference/card_names.csv",
);

/** Lowest ID the mod hands out to user created cards; everything here is machine local. */
const CUSTOM_CARD_ID_MIN = 999990000;

/**
 * Index in this array is the clan number the editor already uses, so it must
 * stay aligned with `classes` in src/data/catalog.ts.
 */
const CLANS = ["ALL", "ELF", "ROYAL", "WITCH", "DRAGON", "NECRO", "VAMPIRE", "BISHOP", "NEMESIS"];
const CHAR_TYPES = ["NORMAL", "SPELL", "FIELD", "CHANT_FIELD"];

/** Characters that would break the `|` separated, newline delimited blob or its template literal. */
const FORBIDDEN_IN_NAME = ["|", "\n", "\r", "`", "\\", "$"];

/**
 * Columns this script needs, in the order CardSkillExporter writes them. Extra
 * trailing columns are tolerated so an export from a newer mod build still works
 * here: `evo_skill_description` was appended for the card reference panel, and
 * nothing in this catalog cares about it.
 */
const EXPECTED_COLUMNS = ["card_id", "card_name", "clan", "char_type", "cost", "atk", "life", "base_card_id", "skill_description"];

function fail(message) {
  console.error(`build-card-catalog: ${message}`);
  process.exit(1);
}

/** Splits one CSV line, honouring "" escaped quotes. Card names are unquoted today, descriptions are not. */
function splitCsvLine(line) {
  const fields = [];
  let field = "";
  let quoted = false;
  for (let index = 0; index < line.length; index++) {
    const character = line[index];
    if (quoted) {
      if (character !== '"') { field += character; continue; }
      if (line[index + 1] === '"') { field += '"'; index++; continue; }
      quoted = false;
      continue;
    }
    if (character === '"') { quoted = true; continue; }
    if (character === ",") { fields.push(field); field = ""; continue; }
    field += character;
  }
  fields.push(field);
  return fields;
}

function integer(text, label, lineNumber) {
  const value = Number(text);
  if (!Number.isInteger(value)) fail(`第 ${lineNumber} 行的 ${label} 不是整数：${JSON.stringify(text)}`);
  return value;
}

const csvPath = process.argv[2] ? resolve(process.argv[2]) : defaultCsvPath;
let csvText;
try {
  csvText = readFileSync(csvPath, "utf8");
} catch (reason) {
  fail(`无法读取 ${csvPath}\n${reason.message}\n启动一次游戏即可生成该文件，或手动传入路径：npm run build:catalog -- <csv path>`);
}

const lines = csvText.replace(/^﻿/, "").split(/\r?\n/).filter((line) => line.length > 0);
if (!lines.length) fail(`${csvPath} 是空文件。`);
const header = splitCsvLine(lines[0]).map((name) => name.trim());
const misplaced = EXPECTED_COLUMNS.filter((name, index) => header[index] !== name);
if (misplaced.length) {
  fail(`表头与 CardSkillExporter 的输出不一致，缺少或错位的列：${misplaced.join(", ")}。\n  期望以 ${EXPECTED_COLUMNS.join(",")} 开头\n  实际：${header.join(",")}`);
}

const allIds = new Set();
const parsed = [];
for (let index = 1; index < lines.length; index++) {
  const lineNumber = index + 1;
  const fields = splitCsvLine(lines[index]);
  if (fields.length < 8) fail(`第 ${lineNumber} 行只有 ${fields.length} 列，至少需要 8 列。`);
  const id = integer(fields[0], "card_id", lineNumber);
  allIds.add(id);
  parsed.push({
    id,
    name: fields[1].trim(),
    clan: fields[2].trim(),
    charType: fields[3].trim(),
    cost: integer(fields[4], "cost", lineNumber),
    atk: integer(fields[5], "atk", lineNumber),
    life: integer(fields[6], "life", lineNumber),
    lineNumber,
  });
}

const foils = parsed.filter((card) => card.id % 10 === 1);
const orphanFoils = foils.filter((card) => !allIds.has(card.id - 1));
if (orphanFoils.length) {
  fail(`${orphanFoils.length} 个闪卡行没有对应的基础卡，丢弃会丢数据。示例：${orphanFoils.slice(0, 5).map((card) => card.id).join(", ")}`);
}

const custom = parsed.filter((card) => card.id >= CUSTOM_CARD_ID_MIN);
const cards = parsed
  .filter((card) => card.id % 10 !== 1 && card.id < CUSTOM_CARD_ID_MIN)
  .sort((left, right) => left.id - right.id);
if (!cards.length) fail("过滤后没有剩下任何卡牌。");

const encoded = [];
let previousId = 0;
for (const card of cards) {
  if (!card.name) fail(`第 ${card.lineNumber} 行（卡牌 ${card.id}）的 card_name 为空。`);
  const offending = FORBIDDEN_IN_NAME.filter((character) => card.name.includes(character));
  if (offending.length) {
    fail(`卡牌 ${card.id} 的名称含有无法编码的字符 ${JSON.stringify(offending)}：${JSON.stringify(card.name)}`);
  }
  const clan = CLANS.indexOf(card.clan);
  if (clan < 0) fail(`卡牌 ${card.id} 的 clan 为未知值 ${JSON.stringify(card.clan)}；请先在 CLANS 和 src/data/catalog.ts 的 classes 中补充。`);
  const charType = CHAR_TYPES.indexOf(card.charType);
  if (charType < 0) fail(`卡牌 ${card.id} 的 char_type 为未知值 ${JSON.stringify(card.charType)}；请先在 CHAR_TYPES 和 src/data/cards.ts 的 cardTypeNames 中补充。`);
  encoded.push([card.id - previousId, card.name, clan, charType, card.cost, card.atk, card.life].join("|"));
  previousId = card.id;
}

const blob = encoded.join("\n");
const generated = `// Generated by scripts/build-card-catalog.mjs from the game's card_names.csv export.
// Do not edit by hand: run \`npm run build:catalog\` against a fresh export instead.
//
// Source rows ${parsed.length} -> ${cards.length} cards (dropped ${foils.length} foil duplicates${custom.length ? ` and ${custom.length} user created card(s)` : ""}).
// Each line is "idDelta|name|clan|charType|cost|atk|life"; idDelta accumulates
// from 0, so line one carries the absolute ID. clan indexes \`classes\` in
// ./catalog, charType indexes \`cardTypeNames\` in ./cards.

/** Clan names as the game spells them, indexed by the clan number used across the editor. */
export const cardClans = ${JSON.stringify(CLANS)} as const;

/** Card types as the game spells them, indexed by the charType number in the blob. */
export const cardTypes = ${JSON.stringify(CHAR_TYPES)} as const;

/** Card count in the blob, so a decode can be checked without walking it twice. */
export const builtInCardCount = ${cards.length};

export const builtInCardBlob = \`${blob}\`;
`;

writeFileSync(outputPath, generated, "utf8");

const kilobytes = (text) => `${(Buffer.byteLength(text, "utf8") / 1024).toFixed(1)} KB`;
console.log(`build-card-catalog: 读取 ${relative(process.cwd(), csvPath) || csvPath}`);
console.log(`  ${parsed.length} 行 -> ${cards.length} 张卡（丢弃 ${foils.length} 个闪卡副本${custom.length ? `、${custom.length} 张用户自制卡 ${custom.map((card) => card.id).join(" ")}` : ""}）`);
console.log(`  写入 ${relative(process.cwd(), outputPath)}（blob ${kilobytes(blob)}，文件 ${kilobytes(generated)}）`);
