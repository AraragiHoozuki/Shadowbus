import { describe, expect, it } from "vitest";
import { emptyCatalog, mergeCatalogEntries, parseCharacterCatalog } from "../src/data/catalog";

describe("内置 ID 目录", () => {
  it("离线包含完整导出角色与 Quest AI", () => {
    const catalog = emptyCatalog();
    expect(catalog.characters.length).toBeGreaterThanOrEqual(418);
    expect(catalog.questAi.length).toBeGreaterThanOrEqual(61);
    expect(catalog.characters.some((item) => item.id === 1 && item.name === "亚里莎")).toBe(true);
  });

  it("本地 Reference 可覆盖内置同 ID 条目", () => {
    const local = parseCharacterCatalog("enemy_chara_id,chara_name,enemy_class,class_name,skin_id\n1,本地亚里莎,1,精灵,1\n");
    const merged = mergeCatalogEntries(emptyCatalog().characters, local, (entry) => entry.id);
    expect(merged.find((item) => item.id === 1)?.name).toBe("本地亚里莎");
  });
});

