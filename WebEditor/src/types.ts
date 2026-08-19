export type JsonRecord = Record<string, unknown>;

export type ValidationSeverity = "error" | "warning";

export interface ValidationIssue {
  severity: ValidationSeverity;
  path: string;
  message: string;
}

export interface WorkspaceFile {
  path: string;
  data: Uint8Array;
  modified: boolean;
}

export interface BossRushAbility extends JsonRecord {
  ability_id: number;
  is_foil: boolean;
  skill: string;
  special_ability_desc: string;
  max_life_change: number;
  life_change: number;
}

export interface BossRushBoss extends JsonRecord {
  name: string;
  enemy_class: number;
  enemy_chara_id: number;
  enemy_emblem_id: number;
  enemy_degree_id: number;
  bossrush_stage_id: number;
  battle3dfield_id: number;
  bgm_id: string;
  enemy_life: number;
  recovery_point: number;
  enemy_skill: string;
  enemy_skills: string[];
  enemy_skill_desc: string;
  enemy_ai_id: number;
  player_first_turn: boolean | null;
  player_start_pp: number;
  enemy_start_pp: number;
  player_start_field_card_ids: number[];
  enemy_start_field_card_ids: number[];
  enemy_sleeve_id: number;
  player_emotion_override: number;
  enemy_emotion_override: number;
  special_battle_id: string;
  id_override_in_battle_log: string;
  token_draw_effect_override: string;
  special_token_draw_effect_override: string;
  vs_effect_override: boolean;
  class_destroy_effect_override: number;
  mission_parameter: Record<string, string>;
  custom_deck_card_ids: number[];
  deck_csv: string;
  style_csv: string;
  emote_csv: string;
  logic_level: number;
  use_inner_emote: boolean;
}

export interface BossRushPackage extends JsonRecord {
  schema_version: number;
  id: string;
  display_name: string;
  detail_title: string;
  detail_text: string;
  ui_theme: string;
  lobby_background: string;
  default_player_life: number;
  initial_progress: number;
  abilities: BossRushAbility[];
  bosses: BossRushBoss[];
  hidden_boss: BossRushBoss | null;
}

export interface CardMasterPatch extends JsonRecord {
  newCard: boolean;
  cardId: number;
  templateCardId: number;
  boolFields: Record<string, boolean>;
  intFields: Record<string, number>;
  intArrayFields: Record<string, number[]>;
  stringChangeFields: Record<string, string>;
  stringAppendFields: Record<string, string>;
  stringArrayFields: Record<string, string[]>;
  localizationFields: Record<string, string>;
  attackEffectFields: AttackEffectFields;
}

/** Normal and evolved attack presentation data stored by CardParameter.AttackEffectParameter. */
export interface AttackEffectFields extends JsonRecord {
  effectPath?: string[];
  se?: string[];
  moveType?: string[];
  effectEnginType?: string[];
  time?: number[];
}

export interface CustomFormat extends JsonRecord {
  id: string;
  displayName: string;
  deckSizeLimit: number | null;
  sameCardLimit: number | null;
  tokenCardTotalLimit: number | null;
  tokenSameCardLimit: number | null;
  cardLimits: Record<string, number>;
}

export interface TwoPickClassRule extends JsonRecord {
  displayName: string | null;
  cardClasses: number[] | null;
  additionalCards: number[];
  description: string | null;
}

export interface TwoPickRoundRule extends JsonRecord {
  rounds: number[];
  costs: number[] | null;
  rarities: number[] | null;
  cards: number[] | null;
}

export interface TwoPickRule extends JsonRecord {
  id: string;
  displayName: string;
  finalDeckSize: number;
  candidateClassCount: number;
  offersPerRound: number;
  cardsPerOffer: number;
  allowDuplicatePicks: boolean;
  sameCardLimit: number | null;
  candidateClasses: number[] | null;
  classRules: Record<string, TwoPickClassRule>;
  roundRules: TwoPickRoundRule[];
  cardPool: number[] | null;
  excludedCards: number[];
  cardWeights: Record<string, number>;
}

export interface CsvDocument {
  headers: string[];
  rows: Record<string, string>[];
  newline: "\r\n" | "\n";
}

export interface EnemyCharacterEntry {
  id: number;
  name: string;
  classId: number;
  className: string;
  skinId?: number;
}

export interface QuestAiEntry {
  enemyAiId: number;
  deckId: number;
  styleId: number;
  emoteId: number;
  logicLevel: number;
  useInnerEmote: boolean;
}

export interface IdCatalog {
  characters: EnemyCharacterEntry[];
  questAi: QuestAiEntry[];
}

export interface EditorDocument<T> {
  path: string;
  value: T;
  sourceText: string;
  dirty: boolean;
}
