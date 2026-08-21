import type { CardEntry, CardMasterPatch } from "../types";
import { builtInCardBlob, builtInCardCount, cardClans, cardTypes } from "./cards.generated";
import { classes } from "./catalog";

/**
 * Card IDs at or above this value are handed out by CardMaster patches rather
 * than the game, so the bundled catalog never contains them and their absence
 * must not be reported as a mistake. Kept in sync with the same constant in
 * scripts/build-card-catalog.mjs.
 */
export const CUSTOM_CARD_ID_MIN = 999990000;

/** Simplified Chinese labels for `cardTypes`, matching the rest of the editor chrome. */
export const cardTypeNames = ["从者", "法术", "护符", "咏唱护符"];

/** Rejects blanks, zero and the placeholder IDs the forms start out with. */
export function normalizeCardId(cardId: number | string | null | undefined): number | null {
  if (cardId == null || cardId === "") return null;
  const value = Number(cardId);
  return Number.isSafeInteger(value) && value > 0 ? value : null;
}

/** Foil printings carry the base card's data and artwork under the base ID + 1. */
export function baseCardId(cardId: number) {
  return cardId % 10 === 1 ? cardId - 1 : cardId;
}

/** True for IDs a user's own CardMaster patch could have created. */
export function isCustomCardId(cardId: number) {
  return cardId >= CUSTOM_CARD_ID_MIN;
}

export function cardClanName(clan: number) {
  return classes.find((item) => item.id === clan)?.name ?? cardClans[clan] ?? `职业 ${clan}`;
}

export function cardTypeName(charType: number) {
  return cardTypeNames[charType] ?? cardTypes[charType] ?? `类型 ${charType}`;
}

/** "龙族 · 从者 · 10 费 6/8"; only minions carry attack and life. */
export function cardSummary(entry: CardEntry) {
  const stats = entry.charType === 0 ? ` ${entry.atk}/${entry.life}` : "";
  return `${cardClanName(entry.clan)} · ${cardTypeName(entry.charType)} · ${entry.cost} 费${stats}`;
}

function decodeCardBlob(blob: string): CardEntry[] {
  const cards: CardEntry[] = [];
  let id = 0;
  /** Split tolerantly: see decodeReferenceBlob — a CRLF checkout must not corrupt the last field. */
  const lines = blob.split(/\r?\n/);
  for (let index = 0; index < lines.length; index++) {
    const line = lines[index];
    if (!line) continue;
    const parts = line.split("|");
    // A short line would desync every following delta, so refuse the whole blob.
    if (parts.length !== 7) throw new Error(`第 ${index + 1} 行有 ${parts.length} 个字段，期望 7 个。`);
    const numbers = [0, 2, 3, 4, 5, 6].map((part) => Number(parts[part]));
    if (numbers.some((value) => !Number.isInteger(value))) throw new Error(`第 ${index + 1} 行含有非整数字段。`);
    id += numbers[0];
    cards.push({ id, name: parts[1], clan: numbers[1], charType: numbers[2], cost: numbers[3], atk: numbers[4], life: numbers[5] });
  }
  return cards;
}

let cachedIndex: Map<number, CardEntry> | null = null;

/**
 * Decodes the bundled blob once. A corrupt blob degrades to an empty index
 * instead of throwing: the editor then behaves as if no catalog existed, which
 * hides names but keeps every form usable and raises no false warnings.
 */
export function builtInCardIndex(): Map<number, CardEntry> {
  if (cachedIndex) return cachedIndex;
  let cards: CardEntry[] = [];
  try {
    cards = decodeCardBlob(builtInCardBlob);
    if (cards.length !== builtInCardCount) throw new Error(`解码得到 ${cards.length} 张卡，期望 ${builtInCardCount} 张。`);
  } catch (reason) {
    console.error("内置卡牌目录解析失败，卡名显示已停用。请重新运行 npm run build:catalog。", reason);
    cards = [];
  }
  cachedIndex = new Map(cards.map((card) => [card.id, card]));
  return cachedIndex;
}

export interface CardCatalog {
  /** Distinct known card IDs; zero means no names can be resolved. */
  readonly size: number;
  get(cardId: number | string | null | undefined): CardEntry | undefined;
}

/**
 * Builds a lookup over the bundled catalog, with `overrides` taking priority so
 * cards defined by the CardMaster file currently open resolve to their own name.
 */
export function createCardCatalog(overrides: readonly CardEntry[] = []): CardCatalog {
  const builtIn = builtInCardIndex();
  const overlay = overrides.length ? new Map(overrides.map((card) => [card.id, card])) : null;
  let extra = 0;
  if (overlay) for (const id of overlay.keys()) if (!builtIn.has(id)) extra++;
  return {
    size: builtIn.size + extra,
    get(cardId) {
      const id = normalizeCardId(cardId);
      if (id == null) return undefined;
      const direct = overlay?.get(id) ?? builtIn.get(id);
      if (direct) return direct;
      const base = baseCardId(id);
      if (base === id) return undefined;
      return overlay?.get(base) ?? builtIn.get(base);
    },
  };
}

/**
 * Reads the cards a CardMaster document creates. A new card inherits its
 * template's parameters unless the patch overrides them, so the entry starts
 * from the template and applies the patch's own name and int fields.
 */
export function cardsFromPatches(patches: readonly CardMasterPatch[]): CardEntry[] {
  const builtIn = builtInCardIndex();
  const entries: CardEntry[] = [];
  for (const patch of patches) {
    if (!patch.newCard) continue;
    const id = normalizeCardId(patch.cardId);
    if (id == null) continue;
    const template = builtIn.get(normalizeCardId(patch.templateCardId) ?? 0);
    const ints = patch.intFields ?? {};
    const number = (key: string, fallback: number) => Number.isFinite(ints[key]) ? Number(ints[key]) : fallback;
    const name = patch.localizationFields?.CardName?.trim() || template?.name;
    entries.push({
      id,
      name: name || `新卡 ${id}`,
      clan: number("Clan", template?.clan ?? 0),
      charType: number("CharType", template?.charType ?? 0),
      cost: number("Cost", template?.cost ?? 0),
      atk: number("Atk", template?.atk ?? 0),
      life: number("Life", template?.life ?? 0),
    });
  }
  return entries;
}
