import Papa from "papaparse";
import type { CsvDocument } from "../types";

export function parseCsv(text: string): CsvDocument {
  const newline = text.includes("\r\n") ? "\r\n" : "\n";
  const parsed = Papa.parse<Record<string, string>>(text.replace(/^\uFEFF/, ""), {
    header: true,
    skipEmptyLines: true,
    transform: (value) => value ?? "",
  });
  if (parsed.errors.length) throw new Error(parsed.errors.map((error) => error.message).join("；"));
  return { headers: parsed.meta.fields ?? [], rows: parsed.data, newline };
}

export function serializeCsv(document: CsvDocument): string {
  return Papa.unparse({ fields: document.headers, data: document.rows.map((row) => document.headers.map((header) => row[header] ?? "")) }, { newline: document.newline });
}

export const deckBaseHeaders = ["CardID", "UseCommon", "CardName", "CardNum", "BattleBonus", "PlayBonus", "Priority"];
export const styleHeaders = ["ID", "Category", "Priority", "Type", "Arg", "Cond"];
export const emoteHeaders = ["ID", "Category", "FaceID", "MotionID", "VoiceID", "TextID"];

export function normalizeDeckCsv(document: CsvDocument): CsvDocument {
  const tagIndexes = document.headers.flatMap((header) => {
    const match = /^Tag(\d+)\.(Type|Arg|Condition)$/i.exec(header);
    return match ? [Number(match[1])] : [];
  });
  const maxTag = Math.max(0, ...tagIndexes);
  const known = new Set([...deckBaseHeaders, "End"]);
  for (let index = 1; index <= maxTag; index++) {
    known.add(`Tag${index}.Type`); known.add(`Tag${index}.Arg`); known.add(`Tag${index}.Condition`);
  }
  const extras = document.headers.filter((header) => !known.has(header));
  const headers = [...deckBaseHeaders];
  for (let index = 1; index <= maxTag; index++) headers.push(`Tag${index}.Type`, `Tag${index}.Arg`, `Tag${index}.Condition`);
  headers.push(...extras, "End");
  return { ...document, headers };
}

export function addDeckTag(document: CsvDocument): CsvDocument {
  const indexes = document.headers.flatMap((header) => /^Tag(\d+)\.Type$/i.exec(header)?.[1] ? [Number(/^Tag(\d+)\.Type$/i.exec(header)![1])] : []);
  const next = Math.max(0, ...indexes) + 1;
  const withoutEnd = document.headers.filter((header) => header !== "End");
  return { ...document, headers: [...withoutEnd, `Tag${next}.Type`, `Tag${next}.Arg`, `Tag${next}.Condition`, "End"] };
}
