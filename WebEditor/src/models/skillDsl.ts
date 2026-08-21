// The bracket skill DSL: `(skill:x)(timing:y)(condition:z)(target:w)(option:v)(preprocess:u)`,
// with several groups joined by commas. BossRush `enemy_skill`/`enemy_skills` and
// ability `skill` all use it, and CardSkillExporter.BuildBracketSkill writes the
// same shape on the C# side.
//
// Parsing and formatting live here rather than in components/Fields.tsx so the
// models can share them without importing React; Fields.tsx re-exports both for
// the editors that already import from there.

export interface SkillDslBlock { key: string; value: string }
export interface SkillDslGroup { blocks: SkillDslBlock[] }
export interface SkillDslParseResult { groups: SkillDslGroup[]; error?: string }

export const defaultSkillDslGroup = (): SkillDslGroup => ({ blocks: [
  { key: "skill", value: "" },
  { key: "timing", value: "" },
  { key: "condition", value: "" },
  { key: "target", value: "" },
  { key: "option", value: "none" },
  { key: "preprocess", value: "none" },
] });

export const dslKeyColors: Record<string, string> = {
  skill: "blue",
  timing: "purple",
  condition: "orange",
  target: "green",
  option: "cyan",
  preprocess: "magenta",
};

export function parseSkillDsl(input: string): SkillDslParseResult {
  const groups: SkillDslGroup[] = [];
  let blocks: SkillDslBlock[] = [];
  let cursor = 0;
  const pushGroup = () => {
    if (blocks.length) groups.push({ blocks });
    blocks = [];
  };

  while (cursor < input.length) {
    while (cursor < input.length && /\s/.test(input[cursor])) cursor++;
    if (cursor >= input.length) break;
    if (input[cursor] === ",") { pushGroup(); cursor++; continue; }
    if (input[cursor] !== "(") return { groups: [], error: `第 ${cursor + 1} 个字符不是“(”，无法识别 DSL 字段。` };

    const start = cursor;
    let depth = 0;
    let quote: string | null = null;
    let end = -1;
    for (; cursor < input.length; cursor++) {
      const character = input[cursor];
      if (character === "\\") { cursor++; continue; }
      if (quote) {
        if (character === quote) quote = null;
        continue;
      }
      if (character === '"' || character === "'") { quote = character; continue; }
      if (character === "(") depth++;
      if (character === ")") {
        depth--;
        if (depth === 0) { end = cursor; break; }
      }
    }
    if (end < 0) return { groups: [], error: `从第 ${start + 1} 个字符开始的 DSL 字段缺少右括号。` };

    const body = input.slice(start + 1, end);
    const separator = body.indexOf(":");
    if (separator < 1) return { groups: [], error: `第 ${start + 1} 个字符开始的 DSL 字段缺少“字段名:字段值”分隔符。` };
    const key = body.slice(0, separator).trim();
    if (!key) return { groups: [], error: `第 ${start + 1} 个字符开始的 DSL 字段名为空。` };
    blocks.push({ key, value: body.slice(separator + 1).trim() });
    cursor = end + 1;
  }
  pushGroup();
  return { groups };
}

export function formatSkillDsl(groups: SkillDslGroup[]) {
  return groups
    .map((group) => group.blocks.map((block) => `(${block.key.trim()}:${block.value.trim()})`).join(""))
    .filter(Boolean)
    .join(",");
}
