import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { App } from "../src/App";
import { CardIdTooltip, CardNameLabel } from "../src/components/Fields";
import { NumberListEditor, NumberMapEditor } from "../src/components/Collections";
import { CardMasterEditor } from "../src/editors/CardMasterEditor";
import { normalizeCardMaster } from "../src/models/normalize";

describe("应用外壳", () => {
  it("呈现五类配置模块和工作区入口", () => {
    render(<App />);
    expect(screen.getByText("Shadowbus 配置工作台")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "打开 Mods 目录" })).toBeInTheDocument();
    for (const label of ["BossRush", "AIData", "CardMaster", "Format", "TwoPick"]) expect(screen.getAllByText(label).length).toBeGreaterThan(0);
  });
});

describe("卡牌名称显示", () => {
  it("分别显示卡名、闪卡、未知 ID、自制 ID 和空值", () => {
    render(<>
      <CardNameLabel cardId={100011010} />
      <CardNameLabel cardId={100011011} />
      <CardNameLabel cardId={123456780} />
      <CardNameLabel cardId={999990004} />
      <CardNameLabel cardId={0} />
    </>);
    expect(screen.getAllByText("哥布林")).toHaveLength(2);
    expect(screen.getByText("未知卡牌")).toBeInTheDocument();
    expect(screen.getByText("自制卡牌")).toBeInTheDocument();
    expect(screen.getByText("未设置")).toBeInTheDocument();
  });

  it("卡牌列表标题统计已识别的张数", () => {
    render(<NumberListEditor label="敌方牌组" field="custom_deck_card_ids" value={[100011010, 123456780]} cardIds onChange={() => {}} />);
    expect(screen.getByText(/custom_deck_card_ids · 2 项 · 已识别 1\/2/)).toBeInTheDocument();
  });

  it("普通数字列表不显示卡牌统计", () => {
    render(<NumberListEditor label="适用轮次" field="rounds" value={[1, 2]} onChange={() => {}} />);
    expect(screen.getByText("rounds · 2 项")).toBeInTheDocument();
  });

  it("展开后每张卡显示卡名并保留可编辑的 ID", async () => {
    render(<NumberListEditor label="敌方牌组" field="custom_deck_card_ids" value={[100011010, 123456780]} cardIds onChange={() => {}} />);
    fireEvent.click(screen.getByText("敌方牌组"));
    expect(await screen.findByText("哥布林")).toBeInTheDocument();
    expect(screen.getByText("未知卡牌")).toBeInTheDocument();
    expect(screen.getByDisplayValue("100011010")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "删除第 1 项" })).toBeInTheDocument();
  });

  it("卡牌字典的键旁显示卡名", async () => {
    render(<NumberMapEditor label="个别卡牌限制" field="cardLimits" value={{ "100011010": 1 }} cardIds onChange={() => {}} />);
    fireEvent.click(screen.getByText("个别卡牌限制"));
    expect(await screen.findByText("哥布林")).toBeInTheDocument();
    expect(screen.getByDisplayValue("100011010")).toBeInTheDocument();
  });
});

describe("CardMaster 技能编辑器", () => {
  const skillPatch = (fields: Record<string, string>) => normalizeCardMaster([{ newCard: false, cardId: 0, templateCardId: 100011010, stringChangeFields: fields }]);
  const evolution = {
    Skill: "none//damage,heal",
    SkillTiming: "none//when_evolve,when_evolve",
    SkillCondition: "none//character=op,none",
    SkillTarget: "none//character=op,character=me",
    SkillOption: "none//damage=2,healing=2",
    SkillPreprocess: "none//none,none",
  };

  // Queried by text rather than by role: computing accessible names for every
  // button of this form takes seconds in jsdom and trips the test timeout.
  it("把 // 两侧拆成普通形态和进化形态两组", () => {
    render(<CardMasterEditor value={skillPatch(evolution)} onChange={() => {}} />);
    expect(screen.getByText(/1 个技能 · 进化前生效/)).toBeInTheDocument();
    expect(screen.getByText(/2 个技能 · 进化后生效/)).toBeInTheDocument();
    expect(screen.getByText("移除进化形态")).toBeInTheDocument();
    expect(screen.getAllByText("新增技能")).toHaveLength(2);
  });

  it("没有 // 时提供添加进化形态的入口", () => {
    render(<CardMasterEditor value={skillPatch({ Skill: "destroy", SkillTiming: "when_activate", SkillCondition: "none", SkillTarget: "none", SkillOption: "none", SkillPreprocess: "none" })} onChange={() => {}} />);
    expect(screen.getByText(/1 个技能 · 进化前生效/)).toBeInTheDocument();
    expect(screen.queryByText(/进化后生效/)).not.toBeInTheDocument();
    expect(screen.getByText(/添加进化形态技能/)).toBeInTheDocument();
    expect(screen.getAllByText("新增技能")).toHaveLength(1);
  });

  it("标示技能字段写在哪个 map 中", () => {
    render(<CardMasterEditor value={skillPatch(evolution)} onChange={() => {}} />);
    expect(screen.getByText(/stringChangeFields 中的六个并行技能字段/)).toBeInTheDocument();
    expect(screen.getByText("完全替换模板技能")).toBeInTheDocument();
  });
});

describe("CardMaster 导入 DSL", () => {
  const skillPatch = (fields: Record<string, string>) => normalizeCardMaster([{ newCard: false, cardId: 0, templateCardId: 100011010, stringChangeFields: fields }]);
  const single = { Skill: "destroy", SkillTiming: "when_activate", SkillCondition: "none", SkillTarget: "none", SkillOption: "none", SkillPreprocess: "none" };
  const evolution = {
    Skill: "none//damage",
    SkillTiming: "none//when_evolve",
    SkillCondition: "none//none",
    SkillTarget: "none//character=op",
    SkillOption: "none//damage=2",
    SkillPreprocess: "none//none",
  };
  const dslBox = () => screen.getByPlaceholderText(/^\(skill:damage\)/);

  /** Opens the one import dialog a card without an evolution form has. */
  function openImport(fields: Record<string, string>) {
    const onChange = vi.fn();
    render(<CardMasterEditor value={skillPatch(fields)} onChange={onChange} />);
    fireEvent.click(screen.getByText("导入 DSL"));
    return onChange;
  }

  it("把粘贴的 DSL 解析成六个字段并追加到已有技能之后", () => {
    const onChange = openImport(single);
    fireEvent.change(dslBox(), { target: { value: "(skill:heal)(timing:when_activate)(condition:none)(target:character=me)(option:healing=2)(preprocess:none)" } });
    // The preview names the form it will land in, so an import cannot silently go
    // to the wrong half.
    expect(screen.getByText("普通形态 第 1 个")).toBeInTheDocument();

    fireEvent.click(screen.getByText("追加 1 个技能"));
    expect(onChange).toHaveBeenCalledTimes(1);
    const fields = onChange.mock.calls[0][0][0].stringChangeFields;
    expect(fields.Skill).toBe("destroy,heal");
    expect(fields.SkillOption).toBe("none,healing=2");
    // Appended, never replaced: the original entry keeps its slot in all six fields.
    expect(fields.SkillTiming).toBe("when_activate,when_activate");
  });

  it("一次导入多个技能组", () => {
    const onChange = openImport(single);
    fireEvent.change(dslBox(), { target: { value: "(skill:heal)(timing:when_activate),(skill:draw)(timing:when_play)" } });
    fireEvent.click(screen.getByText("追加 2 个技能"));
    expect(onChange.mock.calls[0][0][0].stringChangeFields.Skill).toBe("destroy,heal,draw");
  });

  it("演出字段列出来但不导入，因为六个字段里没有它们的位置", () => {
    openImport(single);
    fireEvent.change(dslBox(), { target: { value: "(skill:heal)(timing:when_activate)(effect_path:effect/heal)(se_path:se/heal)" } });
    expect(screen.getByText("以下字段不会被导入")).toBeInTheDocument();
    expect(screen.getByText("effect_path")).toBeInTheDocument();
    expect(screen.getByText("se_path")).toBeInTheDocument();
    // Still importable: the six recognised fields are what the editor manages.
    expect(screen.getByText("追加 1 个技能")).toBeInTheDocument();
  });

  it("值里含逗号时拒绝导入，不让字段错位", () => {
    openImport(single);
    fireEvent.change(dslBox(), { target: { value: "(skill:damage)(condition:count_over(me.hand,3))" } });
    expect(screen.getByText("无法解析")).toBeInTheDocument();
    expect(screen.getByText(/skill_effect_condition/)).toBeInTheDocument();
    expect(screen.getByText("追加技能").closest("button")).toBeDisabled();
  });

  it("进化形态有自己的导入入口", () => {
    render(<CardMasterEditor value={skillPatch(evolution)} onChange={() => {}} />);
    const buttons = screen.getAllByText("导入 DSL");
    expect(buttons).toHaveLength(2);
    fireEvent.click(buttons[1]);
    expect(screen.getByText("导入 DSL 到进化形态")).toBeInTheDocument();
  });
});

describe("card portal tooltip", () => {
  it("shows the portal URL for a valid card ID", async () => {
    render(<CardIdTooltip cardId={100011010}><button type="button">card</button></CardIdTooltip>);
    const button = screen.getByRole("button", { name: "card" });
    fireEvent.mouseEnter(button.parentElement!);
    const link = await screen.findByRole("link", { name: "Card 100011010" });
    expect(link).toHaveAttribute("href", "https://shadowverse-portal.com/card/100011010?lang=zh-tw");
    expect(link).toHaveAttribute("target", "_blank");
    expect(screen.getByRole("img", { name: "Card 100011010" })).toHaveAttribute("src", "https://svgdb.me/assets/cards/jp/C_100011010.png");
    expect(screen.getByText("哥布林")).toBeInTheDocument();
    expect(screen.getByText("中立 · 从者 · 1 费 1/2")).toBeInTheDocument();
  });

  it("converts a foil card ID to its normal image ID", async () => {
    render(<CardIdTooltip cardId={129724011}><button type="button">foil card</button></CardIdTooltip>);
    const button = screen.getByRole("button", { name: "foil card" });
    fireEvent.mouseEnter(button.parentElement!);
    expect(await screen.findByRole("img", { name: "Card 129724010" })).toHaveAttribute("src", "https://svgdb.me/assets/cards/jp/C_129724010.png");
  });
});
