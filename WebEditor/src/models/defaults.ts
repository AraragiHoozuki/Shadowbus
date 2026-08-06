import type { BossRushAbility, BossRushBoss, BossRushPackage, CardMasterPatch, CustomFormat, TwoPickRule } from "../types";

export const newAbility = (): BossRushAbility => ({
  ability_id: 100011020,
  is_foil: false,
  skill: "",
  special_ability_desc: "",
  max_life_change: 0,
  life_change: 0,
});

export const newBoss = (name = "新 Boss"): BossRushBoss => ({
  name,
  enemy_class: 1,
  enemy_chara_id: 1,
  enemy_emblem_id: 0,
  enemy_degree_id: 0,
  bossrush_stage_id: 1,
  battle3dfield_id: 1,
  bgm_id: "",
  enemy_life: 20,
  recovery_point: 0,
  enemy_skill: "",
  enemy_skills: [],
  enemy_skill_desc: "",
  enemy_ai_id: 1,
  player_first_turn: null,
  player_start_pp: 0,
  enemy_start_pp: 0,
  player_start_field_card_ids: [],
  enemy_start_field_card_ids: [],
  enemy_sleeve_id: 3000011,
  player_emotion_override: 0,
  enemy_emotion_override: 0,
  special_battle_id: "",
  id_override_in_battle_log: "",
  token_draw_effect_override: "",
  special_token_draw_effect_override: "",
  vs_effect_override: false,
  class_destroy_effect_override: 0,
  mission_parameter: {},
  custom_deck_card_ids: [],
  deck_csv: "",
  style_csv: "",
  emote_csv: "",
  logic_level: 1,
  use_inner_emote: true,
});

export const newBossRush = (id = "new_bossrush"): BossRushPackage => ({
  schema_version: 5,
  id,
  display_name: "新 BossRush",
  detail_title: "挑战详情",
  detail_text: "",
  ui_theme: "grand_prix_1",
  lobby_background: "",
  default_player_life: 20,
  initial_progress: 0,
  abilities: [newAbility()],
  bosses: [newBoss("第一战")],
  hidden_boss: null,
});

export const newCardPatch = (): CardMasterPatch => ({
  newCard: false,
  cardId: 0,
  templateCardId: 100011010,
  boolFields: {},
  intFields: {},
  stringChangeFields: {},
  stringAppendFields: {},
  stringArrayFields: {},
  localizationFields: {},
  // Optional: an empty object preserves the template's attack presentation.
  // Values are populated when a card is exported from the game or explicitly edited.
  attackEffectFields: {},
});

export const newFormat = (id = "new_format"): CustomFormat => ({
  id,
  displayName: "新赛制",
  deckSizeLimit: null,
  sameCardLimit: null,
  tokenCardTotalLimit: null,
  tokenSameCardLimit: null,
  cardLimits: {},
});

export const newTwoPick = (id = "new_twopick"): TwoPickRule => ({
  id,
  displayName: "新双选规则",
  finalDeckSize: 30,
  candidateClassCount: 3,
  offersPerRound: 2,
  cardsPerOffer: 2,
  allowDuplicatePicks: true,
  sameCardLimit: null,
  candidateClasses: [1, 2, 3, 4, 5, 6, 7, 8],
  classRules: {},
  roundRules: [],
  cardPool: null,
  excludedCards: [],
  cardWeights: {},
});
