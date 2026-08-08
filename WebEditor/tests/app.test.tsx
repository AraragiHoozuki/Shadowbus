import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { App } from "../src/App";
import { CardIdTooltip } from "../src/components/Fields";

describe("应用外壳", () => {
  it("呈现五类配置模块和工作区入口", () => {
    render(<App />);
    expect(screen.getByText("Shadowbus 配置工作台")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "打开 Mods 目录" })).toBeInTheDocument();
    for (const label of ["BossRush", "AIData", "CardMaster", "Format", "TwoPick"]) expect(screen.getAllByText(label).length).toBeGreaterThan(0);
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
  });

  it("converts a foil card ID to its normal image ID", async () => {
    render(<CardIdTooltip cardId={129724011}><button type="button">foil card</button></CardIdTooltip>);
    const button = screen.getByRole("button", { name: "foil card" });
    fireEvent.mouseEnter(button.parentElement!);
    expect(await screen.findByRole("img", { name: "Card 129724010" })).toHaveAttribute("src", "https://svgdb.me/assets/cards/jp/C_129724010.png");
  });
});
