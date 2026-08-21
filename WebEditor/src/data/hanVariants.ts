import { hanSimplified, hanTraditional } from "./hanVariants.generated";

/**
 * Folds traditional Han characters to their simplified forms so a search matches
 * across both scripts.
 *
 * Search folds the query and the indexed text the same way and compares in folded
 * space, which makes matching work in both directions from a single table: a
 * traditional query folds onto simplified text, and a simplified query is already
 * folded so it finds traditional text. This matters because the bundled card data
 * is whatever language the exporting installation ran in — currently traditional
 * Chinese — while the editor's own UI is simplified, so without this a simplified
 * query finds nothing for a third of the characters in the table.
 *
 * Folding is lossy on purpose. 發 and 髮 both fold to 发, so a query of 发 finds
 * both; for search that is the desired behaviour, and it is why only this
 * direction is generated — the reverse is one to many and cannot be a table.
 *
 * Never use this for anything that gets written back to a file. It is a search
 * key, not a translation: the game needs the card's own spelling.
 */

/** Guarded against a truncated table rather than trusting the generator blindly. */
const width = Math.min(hanTraditional.length, hanSimplified.length);

const folded = new Map<string, string>();
for (let index = 0; index < width; index++) folded.set(hanTraditional[index], hanSimplified[index]);

/**
 * The block the table covers. Restricting the scan to it keeps the common case —
 * a query of Latin skill field names — from touching the map at all.
 */
const HAN = /[一-鿿]/g;

export function foldHan(text: string) {
  return text.replace(HAN, (character) => folded.get(character) ?? character);
}

/** How many characters the table folds, so a test can check it was generated at all. */
export const hanFoldSize = folded.size;
