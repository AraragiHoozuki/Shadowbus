// @vitest-environment node
import { describe, expect, it } from "vitest";
import { unzipSync } from "fflate";
import { ImportedWorkspaceAdapter } from "../src/workspace/workspace";

const decode = (value: Uint8Array) => new TextDecoder().decode(value);

describe("导入工作区", () => {
  it("编辑文本时保持无关二进制文件逐字节不变", async () => {
    const binary = new Uint8Array([0, 255, 1, 128, 42]);
    const workspace = new ImportedWorkspaceAdapter("Mods", [
      { path: "BossRush/demo/bossrush.json", data: new TextEncoder().encode('{"id":"demo"}'), modified: false },
      { path: "Native/plugin.dll", data: binary, modified: false },
    ]);
    await workspace.writeText("BossRush/demo/bossrush.json", '{"id":"updated"}');
    const zip = unzipSync(new Uint8Array(await (await workspace.exportZip()).arrayBuffer()));
    expect([...zip["Native/plugin.dll"]]).toEqual([...binary]);
    expect(decode(zip["BossRush/demo/bossrush.json"])).toBe('{"id":"updated"}');
  });

  it("按目录重命名和删除完整 BossRush 包", async () => {
    const workspace = new ImportedWorkspaceAdapter("Mods", [
      { path: "BossRush/old/bossrush.json", data: new Uint8Array([1]), modified: false },
      { path: "BossRush/old/ai/deck/a.csv", data: new Uint8Array([2]), modified: false },
      { path: "BossRush/other/bossrush.json", data: new Uint8Array([3]), modified: false },
    ]);
    await workspace.renameTree("BossRush/old", "BossRush/new");
    expect(await workspace.listFiles()).toContain("BossRush/new/ai/deck/a.csv");
    expect(await workspace.listFiles()).not.toContain("BossRush/old/bossrush.json");
    await workspace.deleteTree("BossRush/new");
    expect(await workspace.listFiles()).toEqual(["BossRush/other/bossrush.json"]);
  });

  it("复制完整 BossRush 包且不改动来源", async () => {
    const workspace = new ImportedWorkspaceAdapter("Mods", [
      { path: "BossRush/old/bossrush.json", data: new Uint8Array([1]), modified: false },
      { path: "BossRush/old/ai/style/a.csv", data: new Uint8Array([2]), modified: false },
    ]);
    await workspace.copyTree("BossRush/old", "BossRush/copy");
    expect(await workspace.listFiles()).toEqual([
      "BossRush/copy/ai/style/a.csv",
      "BossRush/copy/bossrush.json",
      "BossRush/old/ai/style/a.csv",
      "BossRush/old/bossrush.json",
    ]);
  });
});
