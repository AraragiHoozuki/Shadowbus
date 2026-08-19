import type { EnemyCharacterEntry, IdCatalog, QuestAiEntry } from "../types";
import { builtInCatalog } from "./catalog.generated";

export const classes = [
  { id: 0, name: "中立" },
  { id: 1, name: "精灵" },
  { id: 2, name: "皇家护卫" },
  { id: 3, name: "巫师" },
  { id: 4, name: "龙族" },
  { id: 5, name: "死灵法师" },
  { id: 6, name: "血族" },
  { id: 7, name: "主教" },
  { id: 8, name: "复仇天神" },
] as const;

export const uiThemes = [
  "grand_prix_1",
  "grand_prix_2",
  "colosseum_1",
  "colosseum_2",
  "two_pick",
  "quest",
  "classic",
] as const;

export const cardParameterFields = {
  boolean: ["IsVariableCost", "IsResurgentCard"],
  number: [
    "CardId", "ResourceCardId", "CharType", "Clan", "SummonMoveType", "SummonEffectType",
    "Cost", "Atk", "Life", "EvoAtk", "EvoLife", "ChantCount", "Rarity", "GetRedEther",
    "UseRedEther", "EvoEffectType", "BaseCardId", "NormalCardId", "FoilCardId",
    "SameKindNumMaxInUnlimited", "SameKindNumMaxInCrossoverMainClass",
    "SameKindNumMaxInCrossoverSubClass", "SortIndex",
  ],
  numberArray: ["Tribe"],
  string: [
    "CardHashId", "CardSetId", "Path", "Skill", "SkillTiming", "SkillCondition", "SkillTarget",
    "SkillOption", "SkillPreprocess", "SkillIcon", "SummonEffectPath", "SummonSePath",
    "DestroyEffectPath", "PlayVoice", "EvoVoice", "AtkVoice", "DestroyVoice", "SkillVoice",
  ],
  stringArray: [
    "SkillEffectPath", "SkillSe", "SkillEffectTime", "EvoSkillEffectPath", "EvoSkillSe",
    "EvoSkillEffectTime", "EvolEffectPath", "EvolSePath",
  ],
};

export const localizationKeys = [
  "CardName",
  "Description",
  "EvoDescription",
  "SkillDescription",
  "EvoSkillDescription",
];

export const skillParallelKeys = [
  "Skill",
  "SkillTiming",
  "SkillCondition",
  "SkillTarget",
  "SkillOption",
  "SkillPreprocess",
] as const;

export const builtInCharacters: EnemyCharacterEntry[] = builtInCatalog.characters;

export const emptyCatalog = (): IdCatalog => ({
  characters: [...builtInCatalog.characters],
  questAi: [...builtInCatalog.questAi],
});

export function parseCharacterCatalog(csvText: string): EnemyCharacterEntry[] {
  const lines = csvText.replace(/^\uFEFF/, "").split(/\r?\n/).filter(Boolean);
  if (lines.length < 2) return [];
  return lines.slice(1).flatMap((line) => {
    const columns = line.split(",");
    const id = Number(columns[0]);
    const classId = Number(columns[2]);
    if (!Number.isInteger(id) || !Number.isInteger(classId)) return [];
    return [{ id, name: columns[1] || `角色 ${id}`, classId, className: columns[3] || "", skinId: Number(columns[4]) || undefined }];
  });
}

export function mergeCatalogEntries<T>(builtIn: T[], local: T[], getId: (entry: T) => number): T[] {
  const result = new Map(builtIn.map((entry) => [getId(entry), entry]));
  for (const entry of local) result.set(getId(entry), entry);
  return [...result.values()];
}

export function parseQuestAiCatalog(jsonText: string): QuestAiEntry[] {
  try {
    const value = JSON.parse(jsonText) as { entries?: Record<string, unknown>[] };
    return (value.entries ?? []).flatMap((entry) => {
      const enemyAiId = Number(entry.enemy_ai_id);
      if (!Number.isInteger(enemyAiId)) return [];
      return [{
        enemyAiId,
        deckId: Number(entry.deck_id) || 0,
        styleId: Number(entry.style_id) || 0,
        emoteId: Number(entry.emote_id) || 0,
        logicLevel: Number(entry.logic_level) || 0,
        useInnerEmote: Boolean(entry.use_inner_emote),
      }];
    });
  } catch {
    return [];
  }
}
