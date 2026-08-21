/**
 * Fixture rows for the card reference blob, shared by the data test and the panel
 * test. The column list is spelled out here rather than imported so a reordering
 * in the generator shows up as a failing test instead of a silently shifted
 * fixture.
 */
export const REFERENCE_COLUMNS = [
  "idDelta",
  "skill", "timing", "condition", "target", "option", "preprocess",
  "description", "evoDescription", "effectCondition",
  "effectPath", "sePath", "moveType", "engineType", "effectTime", "targetType",
  "evoEffectPath", "evoSePath", "evoMoveType", "evoEngineType", "evoEffectTime",
];

/** Shortest line the generator emits; everything past it is trimmed when empty. */
const REQUIRED_COLUMNS = 7;

export function referenceRow(delta: number, values: Record<string, string>) {
  const fields = REFERENCE_COLUMNS.map((name) => (name === "idDelta" ? String(delta) : values[name] ?? ""));
  // Mirrors the generator's trailing trim, so the decoder is tested against the
  // shape it actually receives.
  while (fields.length > REQUIRED_COLUMNS && fields[fields.length - 1] === "") fields.pop();
  return fields.join("~");
}

export const SAMPLE_IDS = { vanilla: 100011010, evolveOnly: 100621020, fanfare: 100114010 };

export const SAMPLE_NAMES = new Map([
  [SAMPLE_IDS.vanilla, "哥布林"],
  [SAMPLE_IDS.evolveOnly, "血祭侵略者"],
  [SAMPLE_IDS.fanfare, "妖精之光"],
]);

export const SAMPLE_BLOB = [
  // A vanilla card: no skills at all, so the generator stops after six fields.
  referenceRow(SAMPLE_IDS.vanilla, { skill: "none", timing: "none", condition: "none", target: "none", option: "none", preprocess: "none" }),
  // Effect only after evolving: the text lives in evoDescription, the evo
  // presentation column carries one path per evolution skill, and targetType is
  // halved by `//` because the card master has no evo_ twin for it — copied from
  // the shape of real card 100621020.
  referenceRow(SAMPLE_IDS.evolveOnly - SAMPLE_IDS.vanilla, {
    skill: "none//damage,heal",
    timing: "none//evo_start,evo_start",
    condition: "none//none,none",
    target: "none//enemy_leader,friend_leader",
    option: "none//amount_3,amount_2",
    preprocess: "none//none,none",
    evoDescription: "[u]进化时[/u]：对<<对手主战者>>造成${damage}点伤害，并恢复2点体力。",
    targetType: "none//single,single",
    evoEffectPath: "evo/damage,evo/heal",
  }),
  // One ordinary fanfare, with a skill_effect_condition the six field model has
  // no slot for.
  referenceRow(SAMPLE_IDS.fanfare - SAMPLE_IDS.evolveOnly, {
    skill: "damage",
    timing: "on_play",
    condition: "none",
    target: "enemy_follower",
    option: "amount_2",
    preprocess: "none",
    description: "[ffcd45]入场曲[-]：对一个<<敌方从者>>造成2点伤害。",
    effectCondition: "count_over(me.hand_self.count,3)",
    effectPath: "effect/damage",
    sePath: "se/damage",
    targetType: "follower",
  }),
].join("\n");

/**
 * A traditional Chinese row, which is what the bundled export actually is while
 * the editor's own UI is simplified. Kept out of SAMPLE_BLOB so the search tests
 * above keep their exact match sets, and given its own name because none of the
 * three names in SAMPLE_NAMES differ between the two scripts.
 */
export const TRADITIONAL_ID = 101211010;
export const TRADITIONAL_NAME = "蒼空的騎士";
export const TRADITIONAL_ROW = referenceRow(TRADITIONAL_ID, {
  skill: "damage",
  timing: "on_play",
  condition: "none",
  target: "enemy_follower",
  option: "amount_2",
  preprocess: "none",
  description: "[ffcd45]入場曲[-]：對一個<<敵方從者>>造成2點傷害。",
});
