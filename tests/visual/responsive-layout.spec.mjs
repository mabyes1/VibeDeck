import { expect, test } from "@playwright/test";
import path from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";

const host = process.env.VIBEDECK_TEST_URL || "http://127.0.0.1:5000";
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const sourceWebRoot = path.join(repoRoot, "src/VibeDeck.Host/wwwroot");

function genericSideboardCases() {
  const cases = [];
  for (let width = 280; width <= 1_000; width += 60) {
    for (const ratio of [0.45, 1, 2]) {
      cases.push({
        name: `generic-${width}-${ratio}`,
        width,
        height: Math.max(280, Math.min(1_400, Math.round(width * ratio))),
        device: "galaxy-s23",
      });
    }
  }
  for (let width = 280; width <= 1_600; width += 120) {
    for (const ratio of [0.4, 1.6]) {
      cases.push({
        name: `pc-${width}-${ratio}`,
        width,
        height: Math.max(280, Math.min(1_400, Math.round(width * ratio))),
      });
    }
  }
  for (const [width, height] of [[280, 280], [400, 720], [911, 569], [1_600, 280]]) {
    cases.push({
      name: `linux-remote-${width}x${height}`,
      width,
      height,
      device: "linux-desktop",
    });
  }
  return cases;
}

function einkSideboardCases() {
  const cases = [];
  for (const width of [400, 520, 640, 794, 1_054]) {
    for (const ratio of [0.65, 1.4, 2]) {
      cases.push({
        name: `eink-${width}-${ratio}`,
        width,
        height: Math.max(280, Math.min(1_400, Math.round(width * ratio))),
        device: "boox-go-color-7",
        eink: true,
      });
    }
  }
  return cases;
}

async function openSideboard(page, scenario) {
  if (process.env.VIBEDECK_TEST_SOURCE_ASSETS === "1") {
    await page.route("**/*", route => {
      const url = new URL(route.request().url());
      const relative = decodeURIComponent(url.pathname).replace(/^\/+/, "");
      const sourcePath = path.resolve(sourceWebRoot, relative);
      const isLocalAsset = sourcePath.startsWith(sourceWebRoot + path.sep) &&
        /\.(?:css|html|js|json|svg)$/i.test(sourcePath) && existsSync(sourcePath);
      return isLocalAsset ? route.fulfill({ path: sourcePath }) : route.continue();
    });
  }
  await page.setViewportSize({ width: scenario.width, height: scenario.height });
  const query = new URLSearchParams({
    mode: "sideboard",
    source: "responsive-test",
    preview: String(Date.now()),
  });
  if (scenario.device) {
    query.set("devicePreview", scenario.device);
    query.set("previewTrust", "paired");
  }
  if (scenario.eink) {
    query.set("eink", "1");
    query.set("viewer", "1");
  }
  await page.goto(`${host}/index.html?${query}`, { waitUntil: "domcontentloaded" });
  await page.locator("body.mode-sideboard").waitFor();
}

async function layoutAudit(page) {
  return page.evaluate(() => {
    const visible = element => Boolean(
      element &&
      element.getClientRects().length &&
      getComputedStyle(element).visibility !== "hidden"
    );
    const dashboard = document.querySelector("#systemSideboardPage");
    const overview = document.querySelector(".mobile-overview");
    const shell = document.querySelector(".sideboard-shell");
    const cards = visible(dashboard)
      ? [...dashboard.querySelectorAll("[data-dashboard-key]")].filter(visible)
      : [];
    const overlaps = [];
    for (let left = 0; left < cards.length; left += 1) {
      for (let right = left + 1; right < cards.length; right += 1) {
        const a = cards[left].getBoundingClientRect();
        const b = cards[right].getBoundingClientRect();
        if (
          Math.min(a.right, b.right) - Math.max(a.left, b.left) > 1 &&
          Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 1
        ) {
          overlaps.push([
            cards[left].dataset.dashboardKey,
            cards[right].dataset.dashboardKey,
          ]);
        }
      }
    }

    const structural = [
      document.body,
      document.querySelector("main"),
      document.querySelector(".app-view.active"),
      shell,
      dashboard,
    ].filter(visible);
    const clipped = structural.filter(element => {
      const style = getComputedStyle(element);
      const clipsX = ["hidden", "clip"].includes(style.overflowX) &&
        element.scrollWidth > element.clientWidth + 2;
      const clipsY = ["hidden", "clip"].includes(style.overflowY) &&
        element.scrollHeight > element.clientHeight + 2;
      return clipsX || clipsY;
    }).map(element => ({
      node: element.id || element.className || element.tagName,
      client: [element.clientWidth, element.clientHeight],
      scroll: [element.scrollWidth, element.scrollHeight],
      overflow: getComputedStyle(element).overflow,
    }));

    const exit = document.querySelector(".exit-viewer");
    const exitRect = visible(exit) ? exit.getBoundingClientRect() : null;
    const chromeOverlaps = exitRect ? cards.filter(card => {
      const rect = card.getBoundingClientRect();
      return Math.min(rect.right, exitRect.right) - Math.max(rect.left, exitRect.left) > 1 &&
        Math.min(rect.bottom, exitRect.bottom) - Math.max(rect.top, exitRect.top) > 1;
    }).map(card => card.dataset.dashboardKey) : [];

    const shellRect = shell?.getBoundingClientRect();
    const shellStyle = shell ? getComputedStyle(shell) : null;
    const view = document.querySelector(".sideboard-view");
    const viewRect = view?.getBoundingClientRect();
    const main = document.querySelector("main");
    const mainRect = main?.getBoundingClientRect();
    const wideDescendants = mainRect ? [...main.querySelectorAll("*")]
      .filter(visible)
      .map(element => {
        const rect = element.getBoundingClientRect();
        return {
          node: element.id || element.className || element.tagName,
          left: rect.left,
          right: rect.right,
          clientWidth: element.clientWidth,
          scrollWidth: element.scrollWidth,
        };
      })
      .filter(item => item.left < mainRect.left - 1 || item.right > mainRect.right + 1 || item.scrollWidth > item.clientWidth + 2)
      .slice(0, 12) : [];
    return {
      dashboard: visible(dashboard),
      overview: visible(overview),
      documentOverflowX: document.documentElement.scrollWidth > innerWidth + 1,
      overlaps,
      chromeOverlaps,
      clipped,
      wideDescendants,
      shellSize: shellRect ? [shellRect.width, shellRect.height] : [0, 0],
      geometryDebug: {
        main: mainRect ? [mainRect.left, mainRect.right, main.clientWidth, main.scrollWidth] : null,
        view: viewRect ? [viewRect.left, viewRect.right, view.clientWidth, view.scrollWidth] : null,
        shell: shellRect ? [shellRect.left, shellRect.right, shell.clientWidth, shell.scrollWidth] : null,
        shellWidth: shellStyle?.width,
        shellBoxSizing: shellStyle?.boxSizing,
        shellPadding: shellStyle?.padding,
        shellGutter: shellStyle?.scrollbarGutter,
      },
    };
  });
}

test.describe("continuous responsive Sideboard", () => {
  for (const scenario of genericSideboardCases()) {
    test(scenario.name, async ({ page }) => {
      await openSideboard(page, scenario);
      const audit = await layoutAudit(page);
      expect(audit.documentOverflowX).toBe(false);
      expect(audit.dashboard).not.toBe(audit.overview);
      expect(audit.overlaps).toEqual([]);
      expect(audit.chromeOverlaps).toEqual([]);
      expect(audit.clipped, JSON.stringify({ wide: audit.wideDescendants, geometry: audit.geometryDebug }, null, 2)).toEqual([]);
      expect(audit.shellSize[0]).toBeGreaterThan(0);
      expect(audit.shellSize[1]).toBeGreaterThan(0);
    });
  }
});

test.describe("continuous E-Ink Sideboard", () => {
  for (const scenario of einkSideboardCases()) {
    test(scenario.name, async ({ page }) => {
      await openSideboard(page, scenario);
      const audit = await layoutAudit(page);
      expect(audit.documentOverflowX).toBe(false);
      expect(audit.dashboard).toBe(true);
      expect(audit.overview).toBe(false);
      expect(audit.overlaps).toEqual([]);
      expect(audit.chromeOverlaps).toEqual([]);
      expect(audit.clipped, JSON.stringify(audit.wideDescendants, null, 2)).toEqual([]);
    });
  }
});

test.describe("App theme controls", () => {
  test("appearance controls live in Setup and update shared theme tokens", async ({ page }) => {
    await openSideboard(page, { name: "theme-picker", width: 1_280, height: 800 });
    await expect(page.locator(".setup-panels #appThemeColorA")).toHaveCount(1);
    await expect(page.locator("#sideboardShell #appThemeColorA")).toHaveCount(0);
    await page.locator('[data-app-palette="aurora"]').evaluate(button => button.click());
    await expect(page.locator("#appThemeColorA")).toHaveValue("#155d4a");
    await expect(page.locator("#appThemeColorB")).toHaveValue("#173d57");
    await expect(page.locator("html")).toHaveCSS("--theme-palette-a", "#155d4a");
    await expect(page.locator("#sideboardShell")).toHaveCSS("--side-palette-a", "#155d4a");
    await page.locator("#appThemeBackgroundMode").evaluate(select => {
      select.value = "solid";
      select.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await expect(page.locator("html")).toHaveAttribute("data-theme-background", "solid");
    await page.locator("#appThemeGlassOpacity").evaluate(input => {
      input.value = "10";
      input.dispatchEvent(new Event("input", { bubbles: true }));
    });
    await expect(page.locator("html")).toHaveCSS("--theme-glass", "rgba(255, 255, 255, 0.100)");
  });

  test("E-Ink keeps appearance settings out of the reading surface", async ({ page }) => {
    await openSideboard(page, {
      name: "eink-theme-picker",
      width: 794,
      height: 1_054,
      device: "boox-go-color-7",
      eink: true,
    });
    await expect(page.locator(".appearance-theme-panel")).toBeHidden();
  });

  test("custom wallpaper is compressed, applied, and removable", async ({ page }) => {
    await openSideboard(page, { name: "theme-wallpaper", width: 1_280, height: 800 });
    const hostTheme = {
      colorA: "#4b1f66",
      colorB: "#17344d",
      angle: 132,
      intensity: 72,
      backgroundFit: "cover",
      backgroundPosition: "center",
      backgroundBlur: 0,
      backgroundDim: 24,
      glassOpacity: 7,
      glassBlur: 20,
      glassBorder: 12,
    };
    await page.route("**/api/appearance/background*", async route => {
      const method = route.request().method();
      if (method === "POST") {
        await route.fulfill({
          contentType: "application/json",
          body: JSON.stringify({
            configured: true,
            theme: { ...hostTheme, backgroundMode: "image" },
            hasBackgroundImage: true,
            backgroundUrl: "/api/appearance/background?v=test-wallpaper",
          }),
        });
        return;
      }
      if (method === "DELETE") {
        await route.fulfill({
          contentType: "application/json",
          body: JSON.stringify({
            configured: true,
            theme: { ...hostTheme, backgroundMode: "gradient" },
            hasBackgroundImage: false,
            backgroundUrl: "",
          }),
        });
        return;
      }
      await route.fulfill({ status: 404 });
    });
    const png = Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAFklEQVR4nGMMqFjAwMDAxMDAwMDAAAAQugFsZnyF3gAAAABJRU5ErkJggg==",
      "base64",
    );
    await page.locator("#appThemeBackgroundUpload").setInputFiles({
      name: "wallpaper.png",
      mimeType: "image/png",
      buffer: png,
    });
    await expect(page.locator("html")).toHaveAttribute("data-theme-background", "image");
    await expect(page.locator("html")).toHaveCSS("--theme-background-image", /\/api\/appearance\/background\?v=test-wallpaper/);
    await page.locator("#appThemeBackgroundClear").evaluate(button => button.click());
    await expect(page.locator("html")).toHaveAttribute("data-theme-background", "gradient");
    await expect(page.locator("html")).toHaveCSS("--theme-background-image", "none");
  });

  test("Quota uses the shared glass canvas instead of its legacy opaque skin", async ({ page }) => {
    await openSideboard(page, { name: "quota-glass", width: 1_280, height: 800 });
    await page.locator("#quotaMode").evaluate(button => button.click());
    await page.locator("body.mode-quota").waitFor();
    await expect(page.locator(".quota-shell")).toHaveCSS("background-color", "rgba(0, 0, 0, 0)");
    await expect(page.locator(".quota-shell")).toHaveCSS("background-image", "none");
    await expect(page.locator(".quota-shell")).toHaveCSS("border-top-width", "0px");
    const accountCard = page.locator(".quota-account-card").first();
    await expect(accountCard).toHaveCSS("border-top-width", "1px");
    await expect(accountCard).not.toHaveCSS("background-image", "none");
  });

  test("immersive viewer keeps a reachable glass exit pill above product chrome", async ({ page }) => {
    await openSideboard(page, { name: "viewer-exit-pill", width: 911, height: 569, device: "asus-zenpad-p024" });
    await page.locator("body").evaluate(body => {
      body.classList.add("dashboard-viewer", "viewer-immersive");
    });
    const exit = page.locator("#exitViewer");
    await expect(exit).toBeVisible();
    const geometry = await exit.evaluate(element => {
      const rect = element.getBoundingClientRect();
      return {
        left: rect.left,
        top: rect.top,
        right: rect.right,
        bottom: rect.bottom,
        zIndex: Number.parseInt(getComputedStyle(element).zIndex || "0", 10),
        width: innerWidth,
        height: innerHeight,
      };
    });
    expect(geometry.zIndex).toBeGreaterThan(12);
    expect(geometry.right - geometry.left).toBeGreaterThanOrEqual(48);
    expect(geometry.bottom - geometry.top).toBeGreaterThanOrEqual(48);
    expect(geometry.left).toBeGreaterThanOrEqual(0);
    expect(geometry.top).toBeGreaterThanOrEqual(0);
    expect(geometry.right).toBeLessThanOrEqual(geometry.width);
    expect(geometry.bottom).toBeLessThanOrEqual(geometry.height);
  });
});
