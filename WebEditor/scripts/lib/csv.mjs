// Shared CSV reading for the scripts that regenerate src/data/*.generated.ts.
//
// The game's exports are plain UTF-8 CSV with `""` escaped quotes and no
// embedded newlines: CardSkillExporter replaces CR and LF with spaces before
// writing, and the card master dump does the same. Both scripts therefore treat
// one physical line as one record and fail loudly rather than guessing.

import { readFileSync } from "node:fs";

export function fail(script, message) {
  console.error(`${script}: ${message}`);
  process.exit(1);
}

/** Splits one CSV line, honouring `""` escaped quotes inside quoted fields. */
export function splitCsvLine(line) {
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

/**
 * Reads a CSV into `{ columns, rows }`, where each row is the raw field array
 * and `columns` maps a header name to its index. `required` names that must be
 * present; a missing one means the export format changed and is a hard error.
 */
export function readCsv(script, path, required = []) {
  let text;
  try {
    text = readFileSync(path, "utf8");
  } catch (reason) {
    fail(script, `无法读取 ${path}\n${reason.message}`);
  }
  const lines = text.replace(/^﻿/, "").split(/\r?\n/).filter((line) => line.length > 0);
  if (!lines.length) fail(script, `${path} 是空文件。`);

  const header = splitCsvLine(lines[0]).map((name) => name.trim());
  const columns = new Map(header.map((name, index) => [name, index]));
  const missing = required.filter((name) => !columns.has(name));
  if (missing.length) {
    fail(script, `${path} 缺少必需的列 ${missing.join(", ")}。\n  实际表头：${header.join(",")}`);
  }

  const rows = [];
  for (let index = 1; index < lines.length; index++) {
    const fields = splitCsvLine(lines[index]);
    if (fields.length < header.length) {
      fail(script, `${path} 第 ${index + 1} 行只有 ${fields.length} 列，表头有 ${header.length} 列。`);
    }
    rows.push(fields);
  }
  return { header, columns, rows };
}

/** Reads one field of a row by header name, trimmed. */
export const field = (columns, row, name) => (row[columns.get(name)] ?? "").trim();

export function integer(script, text, label, lineNumber) {
  const value = Number(text);
  if (!Number.isInteger(value)) fail(script, `第 ${lineNumber} 行的 ${label} 不是整数：${JSON.stringify(text)}`);
  return value;
}

export const kilobytes = (text) => `${(Buffer.byteLength(text, "utf8") / 1024).toFixed(1)} KB`;
