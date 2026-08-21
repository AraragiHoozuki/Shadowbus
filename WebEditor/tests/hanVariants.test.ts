import { describe, expect, it } from "vitest";
import { foldHan, hanFoldSize } from "../src/data/hanVariants";
import { hanSimplified, hanTraditional } from "../src/data/hanVariants.generated";

/**
 * The fold is a search key, so what matters is not linguistic correctness but the
 * three properties the search relies on: the two halves of the table stay aligned,
 * one pass is enough, and nothing outside Han is touched.
 */
describe("简繁折叠表", () => {
  it("两个字符串等长且非空，否则映射会整体错位", () => {
    expect(hanTraditional.length).toBe(hanSimplified.length);
    expect(hanFoldSize).toBe(hanTraditional.length);
    // Well under the real count (2473), just enough to catch an empty or truncated
    // generation without failing every time the OS table changes.
    expect(hanFoldSize).toBeGreaterThan(2000);
  });

  it("繁体折叠成简体", () => {
    expect(foldHan("蒼空的騎士")).toBe("苍空的骑士");
    expect(foldHan("疾馳")).toBe("疾驰");
    expect(foldHan("對敵方從者造成2點傷害")).toBe("对敌方从者造成2点伤害");
  });

  it("已经是简体的文本不变，非汉字原样保留", () => {
    expect(foldHan("对敌方从者造成2点伤害")).toBe("对敌方从者造成2点伤害");
    expect(foldHan("(skill:damage)(timing:on_play)")).toBe("(skill:damage)(timing:on_play)");
    expect(foldHan("")).toBe("");
    expect(foldHan("100621020 ＡＢ ｱｲｳ")).toBe("100621020 ＡＢ ｱｲｳ");
  });

  it("折叠两次和折叠一次结果相同", () => {
    // The generator refuses a table where a target is itself a source, which is what
    // makes this hold; assert it on the shipped data too.
    expect(foldHan(foldHan(hanTraditional))).toBe(foldHan(hanTraditional));
    expect(foldHan(hanTraditional)).toBe(hanSimplified);
  });

  it("长度不变，所以折叠不会打乱偏移", () => {
    const text = "蒼空的騎士 damage 100621020";
    expect(foldHan(text)).toHaveLength(text.length);
  });

  it("一简对多繁会合并，这正是搜索想要的", () => {
    // 發 and 髮 are different characters with one simplified form, so a query of 发
    // has to find both rather than neither.
    expect(foldHan("發")).toBe("发");
    expect(foldHan("髮")).toBe("发");
  });
});
