import { describe, expect, it } from "vitest";
import { createCardReference, loadCardReference, searchCardReference, stripCardTextMarkup } from "../src/data/cardReference";
import { cardReferenceCount } from "../src/data/cardReference.generated";
import { cardMasterFieldsSnippet, cardMasterSkillFields, cardSkillBreakdown, cardSkillDsl, skillRowsFromDsl } from "../src/models/cardDsl";
import { emptySkillRow } from "../src/models/skills";
import { REFERENCE_COLUMNS, SAMPLE_BLOB, SAMPLE_IDS, SAMPLE_NAMES, TRADITIONAL_ID, TRADITIONAL_NAME, TRADITIONAL_ROW, referenceRow } from "./fixtures/cardReference";

const reference = createCardReference(SAMPLE_BLOB, REFERENCE_COLUMNS);
const [vanilla, evolveOnly, fanfare] = reference.entries;
const name = (cardId: number) => SAMPLE_NAMES.get(cardId);

describe("技能参考数据解码", () => {
  it("按增量还原 ID，并容忍被裁掉的尾部空列", () => {
    expect(reference.entries.map((entry) => entry.id)).toEqual([SAMPLE_IDS.vanilla, SAMPLE_IDS.evolveOnly, SAMPLE_IDS.fanfare]);
    expect(vanilla.skill).toBe("none");
    expect(vanilla.evoEffectPath).toBe("");
    expect(evolveOnly.evoEffectPath).toBe("evo/damage,evo/heal");
  });

  it("闪卡 ID 回退到基础卡", () => {
    expect(reference.get(SAMPLE_IDS.evolveOnly + 1)?.id).toBe(SAMPLE_IDS.evolveOnly);
    expect(reference.get("100114010")?.id).toBe(SAMPLE_IDS.fanfare);
    expect(reference.get(123456780)).toBeUndefined();
  });

  it("显示用文本去掉标记，但保留 ${} 占位符", () => {
    expect(fanfare.text).toBe("入场曲：对一个敌方从者造成2点伤害。");
    expect(evolveOnly.evoText).toBe("进化时：对对手主战者造成${damage}点伤害，并恢复2点体力。");
    expect(evolveOnly.text).toBe("");
    // The raw column is untouched, so a copy still matches the card master.
    expect(fanfare.description).toContain("[ffcd45]");
    expect(stripCardTextMarkup("[u]<<疾驰>>[/u]")).toBe("疾驰");
  });

  it("行数或列顺序不对时整批拒绝，而不是错位一列", () => {
    expect(() => createCardReference("100011010~none~none~none", REFERENCE_COLUMNS)).toThrow(/字段/);
    expect(() => createCardReference(SAMPLE_BLOB, ["idDelta", "skill"])).toThrow(/列顺序/);
    expect(() => createCardReference(referenceRow(1.5, { skill: "none" }), REFERENCE_COLUMNS)).toThrow(/增量/);
  });

  it("打包数据的记录数与生成脚本一致", async () => {
    const bundled = await loadCardReference();
    expect(bundled.error).toBeUndefined();
    expect(bundled.entries).toHaveLength(cardReferenceCount);
    expect(bundled.get(SAMPLE_IDS.evolveOnly)?.skill).toContain("//");
    // The real card packs both forms' target types into the one column that has
    // no evo_ twin, which is what the fixture above mirrors.
    expect(bundled.get(SAMPLE_IDS.evolveOnly)?.targetType).toBe("none//single,single");
  });
});

describe("技能参考搜索", () => {
  it("空查询不返回任何结果", () => {
    expect(searchCardReference(reference, "   ", name)).toEqual({ matches: [], total: 0 });
  });

  it("纯数字按 ID 搜索", () => {
    const result = searchCardReference(reference, "100621", name);
    expect(result.matches).toHaveLength(1);
    expect(result.matches[0]).toMatchObject({ hit: "id" });
    expect(result.matches[0].entry.id).toBe(SAMPLE_IDS.evolveOnly);
  });

  it("卡名、效果文和技能字段分别标注命中来源", () => {
    expect(searchCardReference(reference, "哥布林", name).matches[0]).toMatchObject({ hit: "name", name: "哥布林" });
    expect(searchCardReference(reference, "敌方从者", name).matches[0]).toMatchObject({ hit: "text" });
    const skillHit = searchCardReference(reference, "evo_start", name);
    expect(skillHit.matches).toHaveLength(1);
    expect(skillHit.matches[0]).toMatchObject({ hit: "skill" });
  });

  it("多个词是与的关系", () => {
    expect(searchCardReference(reference, "damage heal", name).matches.map((match) => match.entry.id)).toEqual([SAMPLE_IDS.evolveOnly]);
    expect(searchCardReference(reference, "damage 不存在的词", name).matches).toHaveLength(0);
  });

  it("先按命中来源排序再截断，命中靠后的卡名不会被丢掉", () => {
    const result = searchCardReference(reference, "damage", name, 1);
    expect(result.total).toBe(2);
    // The fanfare has a smaller ID but only matches on its skill field, so the
    // evolution card's text hit has to win the single slot.
    expect(result.matches[0].entry.id).toBe(SAMPLE_IDS.evolveOnly);
    expect(result.matches[0].hit).toBe("text");
  });
});

/**
 * The bundled data is traditional Chinese and the UI is simplified, so crossing
 * the two scripts is the normal case here rather than an edge one.
 */
describe("简繁互搜", () => {
  const traditional = createCardReference(TRADITIONAL_ROW, REFERENCE_COLUMNS);
  const traditionalName = (cardId: number) => (cardId === TRADITIONAL_ID ? TRADITIONAL_NAME : undefined);

  it("简体查询命中繁体效果文", () => {
    const result = searchCardReference(traditional, "敌方从者造成2点伤害", traditionalName);
    expect(result.matches).toHaveLength(1);
    // Classified as a text hit, not a skill hit: the branch that decides between
    // the two folds as well, or every cross-script match would be mislabelled.
    expect(result.matches[0].hit).toBe("text");
  });

  it("简体查询命中繁体卡名", () => {
    expect(searchCardReference(traditional, "苍空", traditionalName).matches[0]).toMatchObject({ hit: "name", name: TRADITIONAL_NAME });
    // Multiple words still have to all match, in folded space.
    expect(searchCardReference(traditional, "苍空 入场曲", traditionalName).matches).toHaveLength(1);
    expect(searchCardReference(traditional, "苍空 不存在的词", traditionalName).matches).toHaveLength(0);
  });

  it("繁体查询命中简体数据", () => {
    const result = searchCardReference(reference, "敵方從者", name);
    expect(result.matches.map((match) => match.entry.id)).toEqual([SAMPLE_IDS.fanfare]);
    expect(result.matches[0].hit).toBe("text");
  });

  it("拉丁字段名和 ID 不受折叠影响", () => {
    expect(searchCardReference(traditional, "on_play", traditionalName).matches[0]).toMatchObject({ hit: "skill" });
    expect(searchCardReference(traditional, String(TRADITIONAL_ID), traditionalName).matches[0]).toMatchObject({ hit: "id" });
  });
});

/**
 * Real card 820531010 writes `2.5,0,0//0` in skill_effect_time while its
 * evo_skill_effect_time is `0`: a presentation column that *does* have an evo_
 * twin still carries a `//`, and the second half only repeats the twin. Kept out
 * of SAMPLE_BLOB so the search tests keep their exact match sets.
 */
const halvedTwin = createCardReference(
  referenceRow(820531010, {
    skill: "gain_buff,gain_buff,gain_buff//gain_buff",
    timing: "on_play,on_play,on_play//evo_start",
    condition: "none,none,none//none",
    target: "friend_follower,friend_follower,friend_follower//friend_follower",
    option: "amount_1,amount_1,amount_1//amount_1",
    preprocess: "none,none,none//none",
    effectTime: "2.5,0,0//0",
    evoEffectTime: "0",
  }),
  REFERENCE_COLUMNS,
).entries[0];

describe("卡牌技能转 DSL", () => {
  it("按 // 分成两个形态，各自索引自己的演出列", () => {
    const breakdown = cardSkillBreakdown(evolveOnly);
    expect(breakdown.hasEvolution).toBe(true);
    expect(breakdown.normal).toHaveLength(1);
    expect(breakdown.evolved).toHaveLength(2);
    expect(cardSkillDsl(breakdown.evolved)).toBe(
      "(skill:damage)(timing:evo_start)(condition:none)(target:enemy_leader)(option:amount_3)(preprocess:none)(effect_path:evo/damage)(effect_target_type:single)"
      + ",(skill:heal)(timing:evo_start)(condition:none)(target:friend_leader)(option:amount_2)(preprocess:none)(effect_path:evo/heal)(effect_target_type:single)",
    );
  });

  it("skill_effect_target_type 的两半分给两个形态，而不是整串塞给进化前", () => {
    const breakdown = cardSkillBreakdown(evolveOnly);
    // The only presentation column without an evo_ twin, so both halves live here.
    expect(breakdown.normal[0].targetType).toBe("none");
    expect(breakdown.evolved.map((group) => group.targetType)).toEqual(["single", "single"]);
    expect(cardSkillDsl(breakdown.normal)).not.toContain("none//single");
    expect(cardSkillDsl(breakdown.normal)).toBe("(skill:none)(timing:none)(condition:none)(target:none)(option:none)(preprocess:none)");
  });

  it("只在演出列真的有值时追加可选字段", () => {
    const breakdown = cardSkillBreakdown(fanfare);
    expect(breakdown.hasEvolution).toBe(false);
    expect(breakdown.evolved).toHaveLength(0);
    expect(cardSkillDsl(breakdown.normal)).toBe(
      "(skill:damage)(timing:on_play)(condition:none)(target:enemy_follower)(option:amount_2)(preprocess:none)(effect_path:effect/damage)(se_path:se/damage)(effect_target_type:follower)",
    );
    // skill_effect_condition has no DSL slot, so it stays out of the string.
    expect(cardSkillDsl(breakdown.normal)).not.toContain("count_over");
    expect(fanfare.effectCondition).toBe("count_over(me.hand_self.count,3)");
  });

  it("有 evo_ 孪生列的演出字段也可能含 //，只取前半段", () => {
    const breakdown = cardSkillBreakdown(halvedTwin);
    expect(breakdown.normal.map((group) => group.effectTime)).toEqual(["2.5", "0", "0"]);
    // The second half repeats what evo_skill_effect_time already says, so the
    // evolution form reads its own column instead of inheriting `0//0`.
    expect(breakdown.evolved.map((group) => group.effectTime)).toEqual(["0"]);
    expect(cardSkillDsl(breakdown.normal)).toContain("(effect_time:2.5)");
    expect(cardSkillDsl(breakdown.normal)).not.toContain("//");
  });

  it("没有进化技能时进化 DSL 为空", () => {
    expect(cardSkillDsl(cardSkillBreakdown(vanilla).evolved)).toBe("");
  });

  it("字段片段保留 //，是唯一能整卡复制的形式", () => {
    const snippet = JSON.parse(cardMasterFieldsSnippet(evolveOnly));
    expect(snippet.stringChangeFields).toEqual({
      Skill: "none//damage,heal",
      SkillTiming: "none//evo_start,evo_start",
      SkillCondition: "none//none,none",
      SkillTarget: "none//enemy_leader,friend_leader",
      SkillOption: "none//amount_3,amount_2",
      SkillPreprocess: "none//none,none",
    });
  });
});

describe("DSL 转技能字段", () => {
  it("复制出去的 DSL 能原样导回同一张卡的字段", () => {
    const imported = skillRowsFromDsl(cardSkillDsl(cardSkillBreakdown(fanfare).normal));
    expect(imported.error).toBeUndefined();
    expect(imported.rows).toEqual([cardMasterSkillFields(fanfare)]);
    // The presentation keys are a normal part of a copied DSL and simply have no
    // slot in the six fields, so they are reported rather than treated as an error.
    expect(imported.ignored).toEqual(["effect_path", "se_path", "effect_target_type"]);
  });

  it("逗号分隔的多个技能组各成一行", () => {
    const imported = skillRowsFromDsl(cardSkillDsl(cardSkillBreakdown(evolveOnly).evolved));
    expect(imported.rows).toHaveLength(2);
    expect(imported.rows.map((row) => row.Skill)).toEqual(["damage", "heal"]);
    expect(imported.rows.map((row) => row.SkillTarget)).toEqual(["enemy_leader", "friend_leader"]);
    expect(imported.rows[1].SkillTiming).toBe("evo_start");
  });

  it("没写到的字段保持新建技能的默认值", () => {
    const imported = skillRowsFromDsl("(skill:damage)");
    expect(imported.rows).toEqual([{ ...emptySkillRow(), Skill: "damage" }]);
    expect(imported.ignored).toEqual([]);
  });

  it("空输入既不导入也不报错", () => {
    expect(skillRowsFromDsl("   ")).toEqual({ rows: [], ignored: [] });
  });

  it("解析失败时原样转达错误，不导入半个技能", () => {
    const broken = skillRowsFromDsl("skill:damage");
    expect(broken.rows).toEqual([]);
    expect(broken.error).toContain("(");
    expect(skillRowsFromDsl("(skill:damage").error).toContain("右括号");
  });

  it("值里含逗号时整批拒绝，因为六个字段就是用逗号分隔的", () => {
    // A legal DSL value: the parser keeps the balanced parens together, so this is
    // one condition. Written into SkillCondition it would become two entries and
    // shift that field out of step with the other five.
    const refused = skillRowsFromDsl("(skill:damage)(condition:count_over(me.hand,3))");
    expect(refused.rows).toEqual([]);
    expect(refused.error).toContain("逗号");
    expect(refused.error).toContain("skill_effect_condition");
  });

  it("值里含 // 时整批拒绝，形态要分开导入", () => {
    const refused = skillRowsFromDsl("(skill:none//damage)");
    expect(refused.rows).toEqual([]);
    expect(refused.error).toContain("//");
  });

  it("一整组都是演出字段时报错，而不是追加一个空技能", () => {
    const refused = skillRowsFromDsl("(effect_path:effect/damage)(se_path:se/damage)");
    expect(refused.rows).toEqual([]);
    expect(refused.error).toContain("可识别");
  });
});
