import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { CardReferencePanel } from "../src/components/CardReferencePanel";
import { createCardCatalog } from "../src/data/cards";
import { REFERENCE_COLUMNS, SAMPLE_BLOB, SAMPLE_IDS } from "./fixtures/cardReference";

// The real generated chunk is a couple of megabytes and its contents change with
// every game patch; the fixture keeps this test about the panel's behaviour.
vi.mock("../src/data/cardReference.generated", () => ({
  cardReferenceColumns: REFERENCE_COLUMNS,
  cardReferenceCount: 3,
  cardReferenceHasEvoText: true,
  cardReferenceBlob: SAMPLE_BLOB,
}));

const writeText = vi.fn((_text: string) => Promise.resolve());
Object.defineProperty(navigator, "clipboard", { value: { writeText }, configurable: true });

const catalog = createCardCatalog([
  { id: SAMPLE_IDS.evolveOnly, name: "血祭侵略者", clan: 4, charType: 0, cost: 4, atk: 3, life: 3 },
]);

const search = () => screen.getByPlaceholderText("搜索卡名、效果文或技能字段");

async function openPanel() {
  render(<CardReferencePanel cards={catalog} />);
  fireEvent.click(screen.getByLabelText("打开卡牌效果参考"));
  await screen.findByText("卡牌效果参考");
  // The reference decodes in a microtask after the dynamic import resolves.
  await waitFor(() => expect(screen.getByText("3 张")).toBeInTheDocument());
}

describe("悬浮卡牌参考面板", () => {
  it("按需加载数据，未打开时不渲染面板", () => {
    render(<CardReferencePanel cards={catalog} />);
    expect(screen.getByLabelText("打开卡牌效果参考")).toBeInTheDocument();
    expect(screen.queryByText("卡牌效果参考")).not.toBeInTheDocument();
  });

  it("按效果文搜索并展开出 ID、技能字段和 DSL", async () => {
    await openPanel();
    fireEvent.change(search(), { target: { value: "进化时" } });
    await waitFor(() => expect(screen.getByText("血祭侵略者")).toBeInTheDocument());
    expect(screen.getByText(`#${SAMPLE_IDS.evolveOnly}`)).toBeInTheDocument();

    fireEvent.click(screen.getByText("血祭侵略者"));
    await waitFor(() => expect(screen.getByText("进化后 DSL")).toBeInTheDocument());
    // Twice: once as this skill's own copyable DSL, once inside the merged
    // per-form string below it.
    expect(screen.getAllByText(/\(skill:damage\)\(timing:evo_start\)/)).toHaveLength(2);
    // The six fields keep the `//` that the DSL cannot express.
    expect(screen.getByText(/Skill: none\/\/damage,heal/)).toBeInTheDocument();
  });

  it("一键复制进化形态 DSL", async () => {
    await openPanel();
    fireEvent.change(search(), { target: { value: "血祭" } });
    await waitFor(() => expect(screen.getByText("血祭侵略者")).toBeInTheDocument());
    fireEvent.click(screen.getByText("血祭侵略者"));

    // Queried by text rather than role: antd renders the icon as an
    // `aria-label="copy"` image inside the button, so the accessible name is not
    // just the label.
    fireEvent.click(screen.getByText("复制进化 DSL"));
    await waitFor(() => expect(screen.getByText("已复制")).toBeInTheDocument());
    expect(writeText).toHaveBeenCalledWith(expect.stringContaining("(skill:heal)(timing:evo_start)"));
  });

  it("每个技能有自己的 DSL，可以单独复制", async () => {
    await openPanel();
    fireEvent.change(search(), { target: { value: "血祭" } });
    await waitFor(() => expect(screen.getByText("血祭侵略者")).toBeInTheDocument());
    fireEvent.click(screen.getByText("血祭侵略者"));

    // One normal skill and two evolution skills, each labelled by form and position.
    await waitFor(() => expect(screen.getAllByText("复制此技能")).toHaveLength(3));
    expect(screen.getByText("进化前 技能 1")).toBeInTheDocument();
    expect(screen.getByText("进化后 技能 2")).toBeInTheDocument();

    fireEvent.click(screen.getAllByText("复制此技能")[2]);
    await waitFor(() => expect(screen.getByText("已复制")).toBeInTheDocument());
    const copied = writeText.mock.calls.at(-1)![0];
    // The second evolution skill alone. Copying one effect out of a card is the
    // common case, so this must not carry the whole form the way the aggregate does.
    expect(copied).toContain("(skill:heal)");
    expect(copied).not.toContain("(skill:damage)");
    expect(copied).not.toContain(",(skill:");
  });

  it("找不到卡时给出提示，关闭后仍保留查询", async () => {
    await openPanel();
    fireEvent.change(search(), { target: { value: "不存在的卡" } });
    await waitFor(() => expect(screen.getByText("没有匹配 “不存在的卡” 的卡牌")).toBeInTheDocument());

    // jsdom cannot reproduce the bug this button once had — it does not retarget a
    // captured pointer, so a click here always reached the handler. The browser
    // check in tests/e2e/cardReference.spec.ts is the one that guards it.
    fireEvent.click(screen.getByLabelText("收起参考面板"));
    expect(screen.getByLabelText("卡牌效果参考")).not.toBeVisible();

    fireEvent.click(screen.getByLabelText("打开卡牌效果参考"));
    expect(screen.getByLabelText("卡牌效果参考")).toBeVisible();
    expect(search()).toHaveValue("不存在的卡");
  });
});
