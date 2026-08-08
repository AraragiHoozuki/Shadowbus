import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { formatSkillDsl, parseSkillDsl, SkillDslField } from "../src/components/Fields";

afterEach(cleanup);

describe("skill DSL editor", () => {
  it("opens a modal with the current DSL and applies edits", () => {
    const onChange = vi.fn();
    render(<SkillDslField label="技能 DSL" field="skill" value="(skill:draw)(timing:self_turn_start)" onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "编辑技能 DSL" }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    const editor = screen.getByRole("textbox", { name: "技能组 1 字段 1 值" });
    expect(editor).toHaveValue("draw");

    fireEvent.change(editor, { target: { value: "heal" } });
    fireEvent.click(screen.getByRole("button", { name: "应用修改" }));

    expect(onChange).toHaveBeenCalledWith("(skill:heal)(timing:self_turn_start)");
  });

  it("does not change the value when the modal is cancelled", () => {
    const onChange = vi.fn();
    render(<SkillDslField label="技能 DSL" value="(skill:draw)" onChange={onChange} />);

    fireEvent.click(screen.getByRole("button", { name: "编辑技能 DSL" }));
    fireEvent.click(screen.getByRole("button", { name: "原始 DSL" }));
    fireEvent.change(screen.getByRole("textbox", { name: "技能 DSL 原始编辑区" }), { target: { value: "changed" } });
    fireEvent.click(screen.getByRole("button", { name: /取\s*消/ }));

    expect(onChange).not.toHaveBeenCalled();
  });

  it("parses nested values and multiple skill groups without flattening them", () => {
    const source = "(skill:draw)(preprocess:remove_after_action=(count=1)),(skill:heal)(option:add_life=2)";
    const parsed = parseSkillDsl(source);

    expect(parsed.error).toBeUndefined();
    expect(parsed.groups).toHaveLength(2);
    expect(parsed.groups[0].blocks[1]).toEqual({ key: "preprocess", value: "remove_after_action=(count=1)" });
    expect(formatSkillDsl(parsed.groups)).toBe(source);
  });
});
