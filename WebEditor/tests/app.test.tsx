import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "../src/App";

describe("应用外壳", () => {
  it("呈现五类配置模块和工作区入口", () => {
    render(<App />);
    expect(screen.getByText("Shadowbus 配置工作台")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "打开 Mods 目录" })).toBeInTheDocument();
    for (const label of ["BossRush", "AIData", "CardMaster", "Format", "TwoPick"]) expect(screen.getAllByText(label).length).toBeGreaterThan(0);
  });
});
