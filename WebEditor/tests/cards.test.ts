import { describe, expect, it } from "vitest";
import { builtInCardIndex, cardSummary, cardsFromPatches, createCardCatalog, isCustomCardId, normalizeCardId } from "../src/data/cards";
import { builtInCardCount } from "../src/data/cards.generated";
import { normalizeCardMaster } from "../src/models/normalize";
import { newBossRush } from "../src/models/defaults";
import { validateBossRush, validateFormat } from "../src/models/validation";

const cardWarnings = (issues: { path: string; message: string }[]) => issues.filter((issue) => issue.message.includes("不在内置卡表"));

describe("内置卡牌目录", () => {
  it("完整解码打包的卡表", () => {
    const index = builtInCardIndex();
    expect(index.size).toBe(builtInCardCount);
    expect(index.get(100011010)).toEqual({ id: 100011010, name: "哥布林", clan: 0, charType: 0, cost: 1, atk: 1, life: 2 });
    expect(index.get(930844090)?.name).toBe("涅克薩斯的奧義");
  });

  it("闪卡 ID 回退到基础卡，且闪卡副本不占体积", () => {
    const catalog = createCardCatalog();
    expect(builtInCardIndex().has(100011011)).toBe(false);
    expect(catalog.get(100011011)?.name).toBe("哥布林");
    expect(catalog.get(107441021)?.name).toBe("阿吉‧塔哈卡");
  });

  it("拒绝无效 ID，并把自制区间与未知 ID 区分开", () => {
    const catalog = createCardCatalog();
    for (const invalid of [0, -1, "", null, undefined, 1.5, "abc"]) expect(normalizeCardId(invalid)).toBeNull();
    expect(normalizeCardId("100011010")).toBe(100011010);
    expect(catalog.get(123456780)).toBeUndefined();
    expect(catalog.get(999990004)).toBeUndefined();
    expect(isCustomCardId(999990004)).toBe(true);
    expect(isCustomCardId(930844090)).toBe(false);
  });

  it("只有从者在摘要里带攻击和生命", () => {
    const catalog = createCardCatalog();
    expect(cardSummary(catalog.get(107441020)!)).toBe("龙族 · 从者 · 10 费 6/8");
    expect(cardSummary(catalog.get(100114010)!)).toBe("精灵 · 法术 · 2 费");
  });
});

describe("当前 CardMaster 文档中的新卡", () => {
  const patches = normalizeCardMaster([
    { newCard: true, cardId: 999990004, templateCardId: 100011010, localizationFields: { CardName: "能力吞噬者" } },
    { newCard: true, cardId: 999990010, templateCardId: 107441020 },
    { newCard: true, cardId: 999990011, templateCardId: 100011010, intFields: { Cost: 7, Atk: 9 } },
    { newCard: false, cardId: 0, templateCardId: 124541020, localizationFields: { CardName: "DEMO" } },
  ]);
  const entries = cardsFromPatches(patches);

  it("只收录新建的卡，改名现有卡不算新卡", () => {
    expect(entries.map((entry) => entry.id)).toEqual([999990004, 999990010, 999990011]);
  });

  it("缺少 CardName 时沿用模板卡名，intFields 覆盖模板数值", () => {
    expect(entries[0].name).toBe("能力吞噬者");
    expect(entries[1].name).toBe("阿吉‧塔哈卡");
    expect(entries[2]).toMatchObject({ name: "哥布林", clan: 0, cost: 7, atk: 9, life: 2 });
  });

  it("覆盖层优先于内置卡表", () => {
    const catalog = createCardCatalog([...entries, { id: 100011010, name: "改过的哥布林", clan: 0, charType: 0, cost: 1, atk: 1, life: 2 }]);
    expect(catalog.get(999990004)?.name).toBe("能力吞噬者");
    expect(catalog.get(100011010)?.name).toBe("改过的哥布林");
    expect(catalog.size).toBe(builtInCardCount + entries.length);
  });
});

describe("未知卡牌 ID 校验", () => {
  const catalog = createCardCatalog();

  it("新建的默认配置不产生卡表警告", () => {
    expect(cardWarnings(validateBossRush(newBossRush("sample"), catalog))).toHaveLength(0);
  });

  it("同一字段的多个未知 ID 合并为一条警告", () => {
    const value = newBossRush("sample");
    value.bosses[0].custom_deck_card_ids = [100011010, 123456780, 123456790, 123456780];
    const warnings = cardWarnings(validateBossRush(value, catalog));
    expect(warnings).toHaveLength(1);
    expect(warnings[0]).toMatchObject({ severity: "warning", path: "bosses[0].custom_deck_card_ids" });
    expect(warnings[0].message).toContain("123456780、123456790");
    expect(warnings[0].message).not.toContain("100011010");
  });

  it("自制区间的 ID 与缺少卡表时都保持安静", () => {
    const value = newBossRush("sample");
    value.bosses[0].custom_deck_card_ids = [999990004, 999990005];
    expect(cardWarnings(validateBossRush(value, catalog))).toHaveLength(0);

    value.bosses[0].custom_deck_card_ids = [123456780];
    expect(cardWarnings(validateBossRush(value))).toHaveLength(0);
    expect(cardWarnings(validateBossRush(value, catalog))).toHaveLength(1);
  });

  it("卡表警告不会阻止保存", () => {
    const issues = validateFormat({ id: "test", displayName: "测试", deckSizeLimit: null, sameCardLimit: null, tokenCardTotalLimit: null, tokenSameCardLimit: null, cardLimits: { "123456780": 1 } }, catalog);
    expect(cardWarnings(issues)).toHaveLength(1);
    expect(issues.some((issue) => issue.severity === "error")).toBe(false);
  });
});
