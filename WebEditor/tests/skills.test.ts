import { describe, expect, it } from "vitest";
import { applySkillGroups, emptySkillRow, parseSkillGroups, skillFieldShapes, type SkillGroups } from "../src/models/skills";
import { validateCardMaster } from "../src/models/validation";
import { normalizeCardMaster } from "../src/models/normalize";
import { skillParallelKeys } from "../src/data/catalog";

/** Card 100621020: nothing before evolving, then deal 2 damage and heal 2. */
const evolutionCard = {
  Skill: "none//damage,heal",
  SkillTiming: "none//when_evolve,when_evolve",
  SkillCondition: "none//character=op&target=inplay&card_type=unit,none",
  SkillTarget: "none//character=op&target=inplay&card_type=unit&select_count=1,character=me&target=inplay&card_type=class",
  SkillOption: "none//damage=2,healing=2",
  SkillPreprocess: "none//none,none",
};

/** Card 128111030, whose own game data is not parallel: five fields are 4+1, SkillPreprocess is 3+2. */
const inconsistentCard = {
  Skill: "pp_fixeduse,powerup,pp_modifier,evolve//heal",
  SkillTiming: "when_play,when_play,when_play,when_play//when_attack",
  SkillCondition: "pp_count>=5,pp_count>=5,pp_count>=5,pp_count>=5//character=me&target=attacker&attacker=self",
  SkillTarget: "none,character=me&target=self,character=me&target=inplay&card_type=class,character=me&target=self//character=me&target=inplay&card_type=class",
  SkillOption: "fixeduse=5,add_offense=2&add_life=2,add_pp=2,none//healing=2",
  SkillPreprocess: "none,none,none//none,",
};

const patchWith = (map: "stringAppendFields" | "stringChangeFields", fields: Record<string, string>) =>
  normalizeCardMaster([{ newCard: false, cardId: 0, templateCardId: 100011010, [map]: fields }]);

describe("技能字段的 // 语法", () => {
  it("把 // 之前当普通形态、之后当进化形态，而不是当成一个逗号槽", () => {
    const groups = parseSkillGroups(evolutionCard, "change");
    expect(groups.hasEvolution).toBe(true);
    expect(groups.normal).toHaveLength(1);
    expect(groups.evolved).toHaveLength(2);
    expect(groups.normal[0].Skill).toBe("none");
    expect(groups.evolved.map((row) => row.Skill)).toEqual(["damage", "heal"]);
    expect(groups.evolved.map((row) => row.SkillOption)).toEqual(["damage=2", "healing=2"]);
    expect(groups.evolved[1].SkillCondition).toBe("none");
  });

  it("原样往回写，不改动未编辑的字段", () => {
    const groups = parseSkillGroups(evolutionCard, "change");
    expect(applySkillGroups(evolutionCard, groups)).toEqual(evolutionCard);
  });

  it("容忍游戏自带的不对齐数据：读得出来，但会补齐成矩形", () => {
    const groups = parseSkillGroups(inconsistentCard, "change");
    expect(groups.normal).toHaveLength(4);
    expect(groups.evolved).toHaveLength(2);
    // SkillPreprocess 少一个进化前条目、多一个进化后条目，缺的位置读成空串。
    expect(groups.normal[3].SkillPreprocess).toBe("");
    expect(groups.evolved.map((row) => row.SkillPreprocess)).toEqual(["none", ""]);
    // 矩阵模型无法表达参差，写回时短的字段被补齐；这种输入本身会被校验拦下。
    const written = applySkillGroups(inconsistentCard, groups);
    expect(written.SkillPreprocess).toBe("none,none,none,//none,");
    expect(written.Skill).toBe("pp_fixeduse,powerup,pp_modifier,evolve//heal,");
    const issues = validateCardMaster(patchWith("stringChangeFields", inconsistentCard));
    expect(issues.filter((issue) => issue.severity === "error")).toHaveLength(2);
  });

  it("没有 // 时不会凭空写出 //", () => {
    const plain = { Skill: "destroy,powerup", SkillTiming: "when_activate,when_activate", SkillCondition: "none,none", SkillTarget: "none,none", SkillOption: "none,none", SkillPreprocess: "none,none" };
    const groups = parseSkillGroups(plain, "change");
    expect(groups.hasEvolution).toBe(false);
    expect(groups.evolved).toEqual([]);
    const written = applySkillGroups(plain, groups);
    for (const key of skillParallelKeys) expect(written[key]).not.toContain("//");
  });

  it("追加模式识别前置逗号，替换模式把它当成真实的空条目", () => {
    const appended = { Skill: ",skill_geminize", SkillTiming: ",when_activate", SkillCondition: ",character=me", SkillTarget: ",none", SkillOption: ",none", SkillPreprocess: ",use_pp=1" };
    const asAppend = parseSkillGroups(appended, "append");
    expect(asAppend.leadingComma).toBe(true);
    expect(asAppend.normal).toHaveLength(1);
    expect(applySkillGroups(appended, asAppend)).toEqual(appended);

    const asChange = parseSkillGroups(appended, "change");
    expect(asChange.leadingComma).toBe(false);
    expect(asChange.normal).toHaveLength(2);
    expect(asChange.normal[0].Skill).toBe("");
  });

  it("新增进化技能写进 // 之后，而不是接在普通形态末尾", () => {
    const plain = { Skill: "destroy", SkillTiming: "when_activate", SkillCondition: "none", SkillTarget: "none", SkillOption: "none", SkillPreprocess: "none" };
    const groups = parseSkillGroups(plain, "change");
    const added: SkillGroups = { ...groups, hasEvolution: true, evolved: [{ ...emptySkillRow(), Skill: "heal", SkillTiming: "when_evolve" }] };
    const written = applySkillGroups(plain, added);
    expect(written.Skill).toBe("destroy//heal");
    expect(written.SkillTiming).toBe("when_activate//when_evolve");
    expect(written.SkillPreprocess).toBe("none//none");
  });

  it("移除进化形态后 // 一起消失", () => {
    const groups = parseSkillGroups(evolutionCard, "change");
    const written = applySkillGroups(evolutionCard, { ...groups, evolved: [], hasEvolution: false });
    expect(written.Skill).toBe("none");
    for (const key of skillParallelKeys) expect(written[key]).not.toContain("//");
  });

  it("普通形态也清空时六个字段整体移除", () => {
    const groups = parseSkillGroups(evolutionCard, "change");
    const written = applySkillGroups({ ...evolutionCard, Path: "keep" }, { ...groups, normal: [], evolved: [], hasEvolution: false });
    expect(written).toEqual({ Path: "keep" });
  });
});

describe("技能字段校验", () => {
  const messages = (patch: ReturnType<typeof patchWith>) => validateCardMaster(patch).map((issue) => `${issue.severity}:${issue.message}`);

  it("接受真实卡牌的进化写法", () => {
    expect(validateCardMaster(patchWith("stringChangeFields", evolutionCard))).toEqual([]);
  });

  it("// 只出现在部分字段时报错", () => {
    const broken = { ...evolutionCard, SkillTiming: "none,when_evolve,when_evolve" };
    const found = messages(patchWith("stringChangeFields", broken));
    expect(found.some((text) => text.startsWith("error:") && text.includes("必须同时带 //"))).toBe(true);
  });

  it("一个字段出现多个 // 时报错", () => {
    const broken = { ...evolutionCard, Skill: "none//damage//heal" };
    const found = messages(patchWith("stringChangeFields", broken));
    expect(found.some((text) => text.startsWith("error:") && text.includes("含有多个 //"))).toBe(true);
  });

  it("分别检查进化前和进化后的项目数", () => {
    const beforeMismatch = messages(patchWith("stringChangeFields", { ...evolutionCard, Skill: "none,extra//damage,heal" }));
    expect(beforeMismatch.some((text) => text.includes("进化前项目数不一致"))).toBe(true);
    const afterMismatch = messages(patchWith("stringChangeFields", { ...evolutionCard, Skill: "none//damage" }));
    expect(afterMismatch.some((text) => text.includes("进化后项目数不一致"))).toBe(true);
  });

  it("两个 map 同时写技能字段时给出警告而非错误", () => {
    const patch = normalizeCardMaster([{
      newCard: false,
      cardId: 0,
      templateCardId: 100011010,
      stringChangeFields: { Skill: "destroy", SkillTiming: "when_activate", SkillCondition: "none", SkillTarget: "none", SkillOption: "none", SkillPreprocess: "none" },
      stringAppendFields: { Skill: ",powerup", SkillTiming: ",when_activate", SkillCondition: ",none", SkillTarget: ",none", SkillOption: ",none", SkillPreprocess: ",none" },
    }]);
    const issues = validateCardMaster(patch);
    expect(issues.some((issue) => issue.severity === "warning" && issue.message.includes("同时写在"))).toBe(true);
    expect(issues.some((issue) => issue.severity === "error")).toBe(false);
  });

  it("shape 报告按半段拆分计数", () => {
    expect(skillFieldShapes({ Skill: "a,b//c" }, "change")).toEqual([{ key: "Skill", separators: 1, normal: 2, evolved: 1 }]);
    expect(skillFieldShapes({ Skill: "a,b" }, "change")).toEqual([{ key: "Skill", separators: 0, normal: 2, evolved: null }]);
  });
});
