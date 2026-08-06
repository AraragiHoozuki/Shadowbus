import type { BossRushPackage, CardMasterPatch, CsvDocument, CustomFormat, TwoPickRule, ValidationIssue } from "../types";
import { deckBaseHeaders, emoteHeaders, styleHeaders } from "./csv";

const error = (path: string, message: string): ValidationIssue => ({ severity: "error", path, message });
const warning = (path: string, message: string): ValidationIssue => ({ severity: "warning", path, message });

export function validateBossRush(value: BossRushPackage): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  if (!value.id.trim()) issues.push(error("id", "配置 ID 不能为空。"));
  if (!/^[a-zA-Z0-9_-]+$/.test(value.id)) issues.push(error("id", "配置 ID 只能包含字母、数字、- 和 _。"));
  if (!value.bosses.length) issues.push(error("bosses", "至少需要一个主线 Boss。"));
  if (value.default_player_life <= 0) issues.push(error("default_player_life", "玩家初始生命必须大于 0。"));
  if (value.initial_progress < 0 || value.initial_progress >= value.bosses.length) issues.push(error("initial_progress", "初始 Boss 索引必须落在主线 Boss 范围内。"));
  const bosses = [
    ...value.bosses.map((boss, index) => ({ boss, path: `bosses[${index}]` })),
    ...(value.hidden_boss ? [{ boss: value.hidden_boss, path: "hidden_boss" }] : []),
  ];
  bosses.forEach(({ boss, path }) => {
    if (!boss.name.trim()) issues.push(error(`${path}.name`, "Boss 名称不能为空。"));
    if (boss.enemy_life <= 0) issues.push(error(`${path}.enemy_life`, "Boss 生命必须大于 0。"));
    if (boss.enemy_class < 1 || boss.enemy_class > 8) issues.push(error(`${path}.enemy_class`, "职业必须为 1 至 8。"));
    if (boss.enemy_chara_id <= 0) issues.push(error(`${path}.enemy_chara_id`, "角色 ID 必须大于 0。"));
    if (boss.logic_level < 0 || boss.logic_level > 2) issues.push(error(`${path}.logic_level`, "AI 逻辑等级必须为 0 至 2。"));
    if (boss.player_start_pp < 0 || boss.player_start_pp > 10 || boss.enemy_start_pp < 0 || boss.enemy_start_pp > 10) issues.push(error(path, "初始 PP 必须为 0 至 10。"));
    if (boss.player_start_field_card_ids.length > 5 || boss.enemy_start_field_card_ids.length > 5) issues.push(error(path, "开局场面每方最多五张卡。"));
    if (boss.custom_deck_card_ids.length && boss.custom_deck_card_ids.length !== 40) issues.push(warning(`${path}.custom_deck_card_ids`, `敌方牌组当前为 ${boss.custom_deck_card_ids.length} 张，通常应为 40 张。`));
  });
  const abilityIds = new Set<number>();
  value.abilities.forEach((ability, index) => {
    if (ability.ability_id <= 0) issues.push(error(`abilities[${index}].ability_id`, "能力卡 ID 必须大于 0。"));
    if (abilityIds.has(ability.ability_id)) issues.push(warning(`abilities[${index}]`, "能力池中存在重复 ID；只有全部能力取得后才会允许重复候选。"));
    abilityIds.add(ability.ability_id);
  });
  return issues;
}

export function validateCardMaster(value: CardMasterPatch[]): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  const newIds = new Set<number>();
  value.forEach((patch, index) => {
    if (patch.templateCardId <= 0) issues.push(error(`[${index}].templateCardId`, "模板卡 ID 必须大于 0。"));
    if (patch.newCard && patch.cardId <= 0) issues.push(error(`[${index}].cardId`, "新卡 ID 必须大于 0。"));
    if (patch.newCard && newIds.has(patch.cardId)) issues.push(error(`[${index}].cardId`, "同一文件中存在重复的新卡 ID。"));
    if (patch.newCard) newIds.add(patch.cardId);
    const parallel = ["Skill", "SkillTiming", "SkillCondition", "SkillTarget", "SkillOption", "SkillPreprocess"];
    const counts = parallel.filter((key) => key in patch.stringAppendFields).map((key) => patch.stringAppendFields[key].split(",").length);
    if (counts.length && counts.some((count) => count !== counts[0])) issues.push(error(`[${index}].stringAppendFields`, "六个技能并行字段的项目数不一致。"));
  });
  return issues;
}

export function validateFormat(value: CustomFormat): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  if (!/^[a-z0-9_-]+$/.test(value.id)) issues.push(error("id", "赛制 ID 只能包含小写字母、数字、- 和 _。"));
  for (const [key, item] of Object.entries(value)) if (/Limit$/.test(key) && typeof item === "number" && item < 0) issues.push(error(key, "限制不能为负数。"));
  for (const [cardId, limit] of Object.entries(value.cardLimits)) if (Number(cardId) <= 0 || limit < 0) issues.push(error(`cardLimits.${cardId}`, "卡牌 ID 必须为正数，限制必须为非负数。"));
  return issues;
}

export function validateTwoPick(value: TwoPickRule): ValidationIssue[] {
  const issues: ValidationIssue[] = [];
  if (!/^[a-z0-9_-]+$/.test(value.id)) issues.push(error("id", "规则 ID 只能包含小写字母、数字、- 和 _。"));
  if (value.finalDeckSize < 6 || value.finalDeckSize > 200 || value.finalDeckSize % 2) issues.push(error("finalDeckSize", "最终牌组必须是 6 至 200 之间的偶数。"));
  if (value.candidateClassCount !== 3 || value.offersPerRound !== 2 || value.cardsPerOffer !== 2) issues.push(error("layout", "当前原版 UI 只支持 3 个职业、每轮 2 组、每组 2 张。"));
  if ((value.candidateClasses ?? []).length < 3) issues.push(error("candidateClasses", "至少需要三个候选职业。"));
  if (value.sameCardLimit != null && value.sameCardLimit < 1) issues.push(error("sameCardLimit", "同卡上限必须为正数或无限制。"));
  const roundCount = value.finalDeckSize / 2;
  const seen = new Set<number>();
  value.roundRules.forEach((rule, index) => {
    if (!rule.rounds.length) issues.push(error(`roundRules[${index}].rounds`, "每条轮次规则至少包含一个轮次。"));
    rule.rounds.forEach((round) => {
      if (round < 1 || round > roundCount) issues.push(error(`roundRules[${index}].rounds`, `轮次必须在 1-${roundCount} 之间。`));
      if (seen.has(round)) issues.push(error(`roundRules[${index}].rounds`, `第 ${round} 轮被重复配置。`));
      seen.add(round);
    });
    if (rule.rarities?.some((rarity) => rarity < 1 || rarity > 4)) issues.push(error(`roundRules[${index}].rarities`, "稀有度只能为 1 至 4。"));
    if (rule.cards && rule.cards.length > 0 && new Set(rule.cards).size < 4) issues.push(warning(`roundRules[${index}].cards`, "指定卡池少于四张不同卡，无法生成一轮候选。"));
  });
  return issues;
}

export function validateCsv(document: CsvDocument, type: "deck" | "style" | "emote"): ValidationIssue[] {
  const required = type === "deck" ? deckBaseHeaders : type === "style" ? styleHeaders : emoteHeaders;
  const issues = required.filter((header) => !document.headers.includes(header)).map((header) => error("headers", `缺少必要列 ${header}。`));
  if (type === "deck" && !document.headers.includes("End")) issues.push(error("headers", "Deck CSV 必须包含结尾 End 列。"));
  const tagParts = new Map<number, Set<string>>();
  document.headers.forEach((header) => { const match = /^Tag(\d+)\.(Type|Arg|Condition)$/.exec(header); if (match) { const set = tagParts.get(Number(match[1])) ?? new Set(); set.add(match[2]); tagParts.set(Number(match[1]), set); } });
  for (const [index, parts] of tagParts) if (parts.size !== 3) issues.push(error("headers", `Tag${index} 必须同时包含 Type、Arg 和 Condition。`));
  return issues;
}
