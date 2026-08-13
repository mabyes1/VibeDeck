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
