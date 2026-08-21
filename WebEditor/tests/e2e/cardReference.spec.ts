import { expect, test, type Page } from "@playwright/test";

/**
 * Browser-only checks for the floating card reference panel. The jsdom tests in
 * tests/cardReferencePanel.test.tsx already cover rendering and search, so this
 * spec is deliberately limited to the claims jsdom cannot verify:
 *
 *   * the generated data is a separate chunk, not requested until the panel opens;
 *   * the panel is not a modal, so the page underneath stays clickable;
 *   * it stacks above antd's modal layer (1000);
 *   * dragging the header actually moves it (real pointer capture);
 *   * the close button inside that same header still closes — jsdom does not
 *     retarget a captured pointer, so only a real browser can catch that
 *     regression, and its absence here is what let the bug ship once.
 *
 * Card 100621020 is used throughout because it is the worked example in the
 * README: `none//damage,heal`, so it exercises both DSL forms. Searching by ID and
 * by skill field keeps the assertions independent of the export's language.
 */

const CARD_ID = "100621020";
const panelOf = (page: Page) => page.getByRole("dialog", { name: "卡牌效果参考" });

/** Requests for the generated chunk, in both dev (module path) and preview (hashed) form. */
function trackReferenceChunk(page: Page) {
  const urls: string[] = [];
  page.on("request", (request) => {
    if (/cardReference\.generated/.test(request.url())) urls.push(request.url());
  });
  return urls;
}

test("悬浮按钮随时可点，数据只在打开时才下载", async ({ page }) => {
  const chunkRequests = trackReferenceChunk(page);
  await page.goto("/");

  // No workspace is open, which is the point: the panel has to work "任意时刻".
  const fab = page.getByRole("button", { name: "打开卡牌效果参考" });
  await expect(fab).toBeVisible();
  await expect(panelOf(page)).toBeHidden();
  expect(chunkRequests, "参考数据在点开面板前就下载了，懒加载失效").toEqual([]);

  await fab.click();
  await expect(panelOf(page)).toBeVisible();

  // The card count tag only renders once the chunk has decoded.
  await expect(panelOf(page).getByText(/^\d+ 张$/)).toBeVisible({ timeout: 30_000 });
  expect(chunkRequests.length, "参考数据没有作为独立 chunk 请求").toBeGreaterThan(0);
});

test("搜索到卡牌后能展开技能字段并复制 DSL", async ({ page, context }) => {
  await context.grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.goto("/");
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  const panel = panelOf(page);
  await expect(panel.getByText(/^\d+ 张$/)).toBeVisible({ timeout: 30_000 });

  await panel.getByPlaceholder("搜索卡名、效果文或技能字段").fill(CARD_ID);
  const row = panel.locator(".card-ref-row").first();
  await expect(row).toContainText(`#${CARD_ID}`);

  await row.locator(".card-ref-row-head").click();
  await expect(row.locator(".card-ref-row-head")).toHaveAttribute("aria-expanded", "true");
  // The six fields are shown verbatim, `//` included — the one place it survives.
  await expect(row.locator(".card-ref-raw").first()).toContainText("Skill: none//damage,heal");
  // And the two forms are split into separate DSL strings, since the DSL cannot express `//`.
  await expect(row.getByText("进化前 DSL")).toBeVisible();
  await expect(row.getByText("进化后 DSL")).toBeVisible();

  await row.getByRole("button", { name: "复制进化 DSL" }).click();
  await expect(row.getByRole("button", { name: "已复制" })).toBeVisible();
  const copied = await page.evaluate(() => navigator.clipboard.readText());
  // Asserted structurally rather than as one literal string: the exact values come
  // from the game's own export and would change under a regeneration, but these
  // properties are what the DSL conversion has to guarantee.
  expect(copied.match(/\(skill:/g), "进化后应当是两个技能").toHaveLength(2);
  expect(copied).toContain("(skill:damage)");
  expect(copied).toContain("(skill:heal)");
  // The whole point of splitting by form: `//` lives in the raw fields above and
  // must never reach a DSL string, in any field including the presentation ones.
  expect(copied, "DSL 里漏出了 //，说明某个字段没有按形态切分").not.toContain("//");
  // skill_effect_target_type has no evo_ twin, so `single` here can only have come
  // from the post-`//` half being handed to the evolution form.
  expect(copied.match(/\(effect_target_type:single\)/g)).toHaveLength(2);

  const normalDsl = await row.locator(".card-ref-dsl .card-ref-raw").first().innerText();
  expect(normalDsl, "进化前 DSL 里漏出了 //").not.toContain("//");
  expect(normalDsl).not.toBe(copied);

  // Every skill also carries its own DSL: you usually want one effect out of a
  // card rather than all of them, so the merged string above is the extra.
  const skills = row.locator(".card-ref-skill");
  await expect(skills).toHaveCount(3);
  await skills.last().getByRole("button", { name: "复制此技能" }).click();
  await expect(skills.last().getByRole("button", { name: "已复制" })).toBeVisible();
  const single = await page.evaluate(() => navigator.clipboard.readText());
  expect(single.match(/\(skill:/g), "单个技能的 DSL 只应含一个技能").toHaveLength(1);
  expect(single).toContain("(skill:heal)");
  expect(single).not.toContain("//");
  // The last evolution skill, so the merged evolution DSL has to contain it verbatim.
  expect(copied, "单技能 DSL 和合并 DSL 对不上").toContain(single);
});

test("标题栏右上角的关闭按钮能关闭面板", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  const panel = panelOf(page);
  await expect(panel).toBeVisible();

  // A real click, so pointerdown reaches the header's drag handler first. Capturing
  // the pointer there retargets the following pointerup — and with it the
  // synthesised click — to the header, which is how this button stopped working.
  await panel.getByRole("button", { name: "收起参考面板" }).click();
  await expect(panel).toBeHidden();

  // And the guard did not cost the header its drag: grabbing it next to the title
  // still moves the panel.
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  const before = (await panel.boundingBox())!;
  const grip = (await panel.locator(".card-ref-panel-header").boundingBox())!;
  await page.mouse.move(grip.x + 60, grip.y + grip.height / 2);
  await page.mouse.down();
  await page.mouse.move(grip.x + 60 - 130, grip.y + grip.height / 2 - 70, { steps: 6 });
  await page.mouse.up();
  expect(Math.round((await panel.boundingBox())!.x)).toBeLessThan(Math.round(before.x));
});

test("简体和繁体查到同一批卡，与卡表用哪种字体无关", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  const panel = panelOf(page);
  await expect(panel.getByText(/^\d+ 张$/)).toBeVisible({ timeout: 30_000 });

  const search = panel.getByPlaceholder("搜索卡名、效果文或技能字段");
  const count = panel.locator(".card-ref-count");

  // 从 and 從 are different characters and the export only ever contains one of
  // them, so this is the whole feature in one assertion — and it does not depend on
  // which language the data was exported in, because folding sends both queries to
  // the same rows either way.
  await search.fill("从者");
  await expect(count).toContainText(/张匹配/);
  const simplified = await count.innerText();
  expect(simplified, "简体查询没有匹配到任何卡；如果卡表不是中文导出的，这个用例不适用").not.toMatch(/^0 张/);

  await search.fill("從者");
  await expect(count, "简体和繁体查到的卡不一样，说明只折叠了一侧").toHaveText(simplified);
});

test("面板不是模态框：层级在弹窗之上，下层仍可操作", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  const panel = panelOf(page);
  await expect(panel).toBeVisible();

  // Above antd's modal layer, so it stays readable over the skill DSL dialog.
  const zIndex = await panel.evaluate((node) => Number(getComputedStyle(node).zIndex));
  expect(zIndex).toBeGreaterThan(1000);

  // The form underneath still responds, which a Modal's wrapper would prevent
  // even with mask={false}. Scoped to the menu because switching modules also puts
  // the name in the breadcrumb.
  await page.getByRole("menu").getByText("CardMaster", { exact: true }).click();
  await expect(page.getByRole("listitem").getByText("CardMaster", { exact: true })).toBeVisible();
  await expect(panel).toBeVisible();

  // Escape closes the panel even though focus is now on the module list, not in
  // the panel. A handler bound to the panel element alone would never see this,
  // which is the normal case: you keep the panel open *while* editing.
  await page.keyboard.press("Escape");
  await expect(panel).toBeHidden();
  // Reopening keeps the panel mounted, so state survives a close.
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  await expect(panel).toBeVisible();
});

test("拖动标题栏移动面板，拖右下角改变大小", async ({ page }) => {
  await page.goto("/");
  await page.getByRole("button", { name: "打开卡牌效果参考" }).click();
  const panel = panelOf(page);
  await expect(panel).toBeVisible();

  const before = (await panel.boundingBox())!;
  const header = panel.locator(".card-ref-panel-header");
  const grip = (await header.boundingBox())!;
  await page.mouse.move(grip.x + grip.width / 2, grip.y + grip.height / 2);
  await page.mouse.down();
  await page.mouse.move(grip.x + grip.width / 2 - 160, grip.y + grip.height / 2 - 90, { steps: 8 });
  await page.mouse.up();

  const moved = (await panel.boundingBox())!;
  expect(Math.round(moved.x)).toBeLessThan(Math.round(before.x));
  expect(Math.round(moved.y)).toBeLessThan(Math.round(before.y));
  // Moving must not resize.
  expect(Math.round(moved.width)).toBe(Math.round(before.width));

  const handle = (await panel.locator(".card-ref-resize").boundingBox())!;
  await page.mouse.move(handle.x + handle.width / 2, handle.y + handle.height / 2);
  await page.mouse.down();
  await page.mouse.move(handle.x + handle.width / 2 + 120, handle.y + handle.height / 2 + 70, { steps: 8 });
  await page.mouse.up();

  const resized = (await panel.boundingBox())!;
  expect(Math.round(resized.width)).toBeGreaterThan(Math.round(moved.width));
  expect(Math.round(resized.height)).toBeGreaterThan(Math.round(moved.height));
});
