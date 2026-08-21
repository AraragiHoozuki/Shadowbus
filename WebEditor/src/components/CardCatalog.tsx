import { createContext, useContext, type ReactNode } from "react";
import { createCardCatalog, type CardCatalog } from "../data/cards";

const CardCatalogContext = createContext<CardCatalog | null>(null);

let fallbackCatalog: CardCatalog | null = null;

/**
 * Card name lookup for every editor below. The value is built once in App so
 * validation and the forms agree on which cards exist; shared field components
 * read it through `useCardEntry` instead of taking a prop from each editor.
 */
export function CardCatalogProvider({ value, children }: { value: CardCatalog; children: ReactNode }) {
  return <CardCatalogContext.Provider value={value}>{children}</CardCatalogContext.Provider>;
}

/** Without a provider this falls back to the bundled catalog, so fields render standalone. */
export function useCardCatalog(): CardCatalog {
  const provided = useContext(CardCatalogContext);
  if (provided) return provided;
  fallbackCatalog ??= createCardCatalog();
  return fallbackCatalog;
}

export function useCardEntry(cardId: number | string | null | undefined) {
  return useCardCatalog().get(cardId);
}
