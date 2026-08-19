import { describe, expect, it } from "vitest";
import { addDeckTag, normalizeDeckCsv, parseCsv, serializeCsv, styleHeaders } from "../src/models/csv";
import { normalizeBossRush, normalizeCardMaster, normalizeTwoPick } from "../src/models/normalize";
import { validateBossRush, validateCsv, validateTwoPick } from "../src/models/validation";
import { newBossRush, newTwoPick } from "../src/models/defaults";

describe("JSON 模型保真", () => {
  it("保留 BossRush 各层未知字段并补齐已知默认值", () => {
    const value = normalizeBossRush({
      id: "custom",
      future_root: { enabled: true },
      abilities: [{ ability_id: 100, future_ability: 7 }],
      bosses: [{ name: "测试 Boss", future_boss: [1, 2, 3] }],
    });
    expect(value.future_root).toEqual({ enabled: true });
    expect(value.abilities[0].future_ability).toBe(7);
    expect(value.bosses[0].future_boss).toEqual([1, 2, 3]);
    expect(value.bosses[0].enemy_life).toBe(20);
  });

  it("保留 CardMaster 与 TwoPick 的未知字段", () => {
    const card = normalizeCardMaster([{ templateCardId: 1, extension: "keep" }])[0];
    const twoPick = normalizeTwoPick({ id: "test", extension: { a: 1 } });
    expect(card.extension).toBe("keep");
    expect(twoPick.extension).toEqual({ a: 1 });
    const attackCard = normalizeCardMaster([{
      templateCardId: 1,
      attackEffectFields: {
        effectPath: ["normal_attack", "evo_attack"],
        se: ["se_normal", "se_evo"],
        moveType: ["DIRECT", "ARC"],
        effectEnginType: ["SHURIKEN", "SOLID"],
        time: [0.5, 0.75],
      },
    }])[0];
    expect(attackCard.attackEffectFields.effectPath).toEqual(["normal_attack", "evo_attack"]);
    expect(attackCard.attackEffectFields.time).toEqual([0.5, 0.75]);
    const tribeCard = normalizeCardMaster([{
      templateCardId: 1,
      intArrayFields: { Tribe: [2, "7", "invalid"] },
    }])[0];
    expect(tribeCard.intArrayFields.Tribe).toEqual([2, 7]);
  });
});

describe("CSV 往返", () => {
  it("保留引号、换行和未知列", () => {
    const source = 'ID,Category,Priority,Type,Arg,Cond,Extra\r\n1,All,100,unitBonus,"POW ( 2 , 3 )","NOW_TURN >= 2","line 1\nline 2"\r\n';
    const parsed = parseCsv(source);
    expect(parsed.headers.at(-1)).toBe("Extra");
    expect(parsed.rows[0].Extra).toBe("line 1\nline 2");
    const roundTrip = parseCsv(serializeCsv(parsed));
    expect(roundTrip.rows).toEqual(parsed.rows);
  });

  it("Deck 标准化保留未知列并将 End 放到末尾", () => {
    const parsed = parseCsv("CardID,Extra,Tag1.Type,Tag1.Arg,Tag1.Condition,End\n1,x,a,b,c,\n");
    const normalized = normalizeDeckCsv(parsed);
    expect(normalized.headers.at(-1)).toBe("End");
    expect(normalized.headers).toContain("Extra");
    const expanded = addDeckTag(normalized);
    expect(expanded.headers).toContain("Tag2.Type");
  });

  it("使用游戏实际 Style 六列表头", () => {
    expect(styleHeaders).toEqual(["ID", "Category", "Priority", "Type", "Arg", "Cond"]);
    expect(validateCsv({ headers: styleHeaders, rows: [], newline: "\n" }, "style")).toHaveLength(0);
  });
});

describe("阻止无效配置", () => {
  it("检查 BossRush ID 与初始关卡索引", () => {
    const value = newBossRush("bad id");
    value.initial_progress = 3;
    expect(validateBossRush(value).filter((item) => item.severity === "error").length).toBeGreaterThanOrEqual(2);
  });

  it("检查 TwoPick 固定布局和轮次冲突", () => {
    const value = newTwoPick("test");
    value.offersPerRound = 3;
    value.roundRules = [{ rounds: [1], costs: null, rarities: null, cards: null }, { rounds: [1], costs: null, rarities: null, cards: null }];
    expect(validateTwoPick(value).filter((item) => item.severity === "error").length).toBeGreaterThanOrEqual(2);
  });
});
