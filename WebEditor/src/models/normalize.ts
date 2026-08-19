import type { AttackEffectFields, BossRushAbility, BossRushBoss, BossRushPackage, CardMasterPatch, CustomFormat, JsonRecord, TwoPickRule } from "../types";
import { newAbility, newBoss, newBossRush, newCardPatch, newFormat, newTwoPick } from "./defaults";

const object = (value: unknown): JsonRecord => value && typeof value === "object" && !Array.isArray(value) ? value as JsonRecord : {};
const string = (value: unknown, fallback = "") => typeof value === "string" ? value : fallback;
const number = (value: unknown, fallback = 0) => Number.isFinite(Number(value)) ? Number(value) : fallback;
const boolean = (value: unknown, fallback = false) => typeof value === "boolean" ? value : fallback;
const numberArray = (value: unknown) => Array.isArray(value) ? value.map(Number).filter(Number.isFinite) : [];
const stringArray = (value: unknown) => Array.isArray(value) ? value.map((item) => String(item)) : [];
const numberArrayOrDefault = (value: unknown, fallback: number[]) => Array.isArray(value) ? value.map(Number).map((item) => Number.isFinite(item) ? item : 0) : fallback;

function stringMap(value: unknown): Record<string, string> {
  return Object.fromEntries(Object.entries(object(value)).map(([key, item]) => [key, string(item)]));
}
function numberMap(value: unknown): Record<string, number> {
  return Object.fromEntries(Object.entries(object(value)).map(([key, item]) => [key, number(item)]));
}

export function normalizeBoss(value: unknown): BossRushBoss {
  const source = object(value);
  const base = newBoss();
  return {
    ...base,
    ...source,
    name: string(source.name, base.name), enemy_class: number(source.enemy_class, 1), enemy_chara_id: number(source.enemy_chara_id, 1),
    enemy_emblem_id: number(source.enemy_emblem_id), enemy_degree_id: number(source.enemy_degree_id), bossrush_stage_id: number(source.bossrush_stage_id, 1),
    battle3dfield_id: number(source.battle3dfield_id, 1), bgm_id: string(source.bgm_id), enemy_life: number(source.enemy_life, 20),
    recovery_point: number(source.recovery_point), enemy_skill: string(source.enemy_skill), enemy_skills: stringArray(source.enemy_skills),
    enemy_skill_desc: string(source.enemy_skill_desc), enemy_ai_id: number(source.enemy_ai_id, 1),
    player_first_turn: source.player_first_turn == null ? null : boolean(source.player_first_turn), player_start_pp: number(source.player_start_pp),
    enemy_start_pp: number(source.enemy_start_pp), player_start_field_card_ids: numberArray(source.player_start_field_card_ids),
    enemy_start_field_card_ids: numberArray(source.enemy_start_field_card_ids), enemy_sleeve_id: number(source.enemy_sleeve_id, 3000011),
    player_emotion_override: number(source.player_emotion_override), enemy_emotion_override: number(source.enemy_emotion_override),
    special_battle_id: string(source.special_battle_id), id_override_in_battle_log: string(source.id_override_in_battle_log),
    token_draw_effect_override: string(source.token_draw_effect_override), special_token_draw_effect_override: string(source.special_token_draw_effect_override),
    vs_effect_override: boolean(source.vs_effect_override), class_destroy_effect_override: number(source.class_destroy_effect_override),
    mission_parameter: stringMap(source.mission_parameter), custom_deck_card_ids: numberArray(source.custom_deck_card_ids),
    deck_csv: string(source.deck_csv), style_csv: string(source.style_csv), emote_csv: string(source.emote_csv),
    logic_level: number(source.logic_level, 1), use_inner_emote: boolean(source.use_inner_emote, true),
  };
}

export function normalizeBossRush(value: unknown): BossRushPackage {
  const source = object(value);
  const base = newBossRush(string(source.id, "bossrush"));
  const abilities: BossRushAbility[] = (Array.isArray(source.abilities) ? source.abilities : []).map((item) => {
    const row = object(item), defaults = newAbility();
    return { ...defaults, ...row, ability_id: number(row.ability_id), is_foil: boolean(row.is_foil), skill: string(row.skill), special_ability_desc: string(row.special_ability_desc), max_life_change: number(row.max_life_change), life_change: number(row.life_change) };
  });
  return { ...base, ...source, schema_version: number(source.schema_version, 5), id: string(source.id, base.id), display_name: string(source.display_name, base.display_name), detail_title: string(source.detail_title, base.detail_title), detail_text: string(source.detail_text), ui_theme: string(source.ui_theme, "grand_prix_1"), lobby_background: string(source.lobby_background), default_player_life: number(source.default_player_life, 20), initial_progress: number(source.initial_progress), abilities, bosses: (Array.isArray(source.bosses) ? source.bosses : []).map(normalizeBoss), hidden_boss: source.hidden_boss == null ? null : normalizeBoss(source.hidden_boss) };
}

export function normalizeCardMaster(value: unknown): CardMasterPatch[] {
  const list = Array.isArray(value) ? value : [];
  return list.map((item) => {
    const source = object(item), base = newCardPatch();
    const attack = object(source.attackEffectFields);
    const attackEffectFields: AttackEffectFields = {
      ...base.attackEffectFields,
      ...attack,
      effectPath: attack.effectPath == null ? base.attackEffectFields.effectPath : stringArray(attack.effectPath),
      se: attack.se == null ? base.attackEffectFields.se : stringArray(attack.se),
      moveType: attack.moveType == null ? base.attackEffectFields.moveType : stringArray(attack.moveType),
      effectEnginType: attack.effectEnginType == null ? base.attackEffectFields.effectEnginType : stringArray(attack.effectEnginType),
      time: attack.time == null ? base.attackEffectFields.time : numberArrayOrDefault(attack.time, [0, 0]),
    };
    return { ...base, ...source, newCard: boolean(source.newCard), cardId: number(source.cardId), templateCardId: number(source.templateCardId), boolFields: Object.fromEntries(Object.entries(object(source.boolFields)).map(([key, value]) => [key, boolean(value)])), intFields: numberMap(source.intFields), intArrayFields: Object.fromEntries(Object.entries(object(source.intArrayFields)).map(([key, value]) => [key, numberArray(value)])), stringChangeFields: stringMap(source.stringChangeFields), stringAppendFields: stringMap(source.stringAppendFields), stringArrayFields: Object.fromEntries(Object.entries(object(source.stringArrayFields)).map(([key, value]) => [key, stringArray(value)])), localizationFields: stringMap(source.localizationFields), attackEffectFields };
  });
}

export function normalizeFormat(value: unknown): CustomFormat {
  const source = object(value), base = newFormat(string(source.id, "format"));
  const optional = (item: unknown) => item == null ? null : number(item);
  return { ...base, ...source, id: string(source.id, base.id), displayName: string(source.displayName, base.displayName), deckSizeLimit: optional(source.deckSizeLimit), sameCardLimit: optional(source.sameCardLimit), tokenCardTotalLimit: optional(source.tokenCardTotalLimit), tokenSameCardLimit: optional(source.tokenSameCardLimit), cardLimits: numberMap(source.cardLimits) };
}

export function normalizeTwoPick(value: unknown): TwoPickRule {
  const source = object(value), base = newTwoPick(string(source.id, "twopick"));
  const optionalNumbers = (item: unknown) => item == null ? null : numberArray(item);
  return { ...base, ...source, id: string(source.id, base.id), displayName: string(source.displayName, base.displayName), finalDeckSize: number(source.finalDeckSize, 30), candidateClassCount: number(source.candidateClassCount, 3), offersPerRound: number(source.offersPerRound, 2), cardsPerOffer: number(source.cardsPerOffer, 2), allowDuplicatePicks: boolean(source.allowDuplicatePicks, true), sameCardLimit: source.sameCardLimit == null ? null : number(source.sameCardLimit), candidateClasses: optionalNumbers(source.candidateClasses), classRules: Object.fromEntries(Object.entries(object(source.classRules)).map(([key, value]) => { const row = object(value); return [key, { ...row, displayName: row.displayName == null ? null : string(row.displayName), cardClasses: optionalNumbers(row.cardClasses), additionalCards: numberArray(row.additionalCards), description: row.description == null ? null : string(row.description) }]; })), roundRules: (Array.isArray(source.roundRules) ? source.roundRules : []).map((value) => { const row = object(value); return { ...row, rounds: numberArray(row.rounds), costs: optionalNumbers(row.costs), rarities: optionalNumbers(row.rarities), cards: optionalNumbers(row.cards) }; }), cardPool: optionalNumbers(source.cardPool), excludedCards: numberArray(source.excludedCards), cardWeights: numberMap(source.cardWeights) };
}
