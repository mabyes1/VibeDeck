import { expect, test } from "@playwright/test";
import path from "node:path";
import { existsSync } from "node:fs";
import { fileURLToPath } from "node:url";

const host = process.env.VIBEDECK_TEST_URL || "http://127.0.0.1:5000";
const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const sourceWebRoot = path.join(repoRoot, "src/VibeDeck.Host/wwwroot");

const longCopy = "A deliberately long translated status message that must remain reachable without overlapping adjacent controls or being silently clipped.";
const longIdentity = "responsive-layout-verification-with-an-extraordinarily-long-account-identity@example-subdomain.invalid";

function pcCases() {
  return [
    [280, 280],
    [400, 280],
    [520, 720],
    [760, 420],
    [1_000, 400],
    [1_600, 280],
    [1_440, 900],
  ].map(([width, height]) => ({ name: `pc-${width}x${height}`, width, height }));
}

function browserDeviceCases() {
  return [
    { name: "galaxy-narrow", width: 280, height: 568, device: "galaxy-s23" },
    { name: "galaxy-portrait", width: 430, height: 932, device: "galaxy-s23" },
    { name: "galaxy-short", width: 667, height: 280, device: "galaxy-s23" },
    { name: "iphone-portrait", width: 375, height: 812, device: "iphone-xs" },
    { name: "iphone-landscape", width: 812, height: 375, device: "iphone-xs" },
    { name: "asus-portrait", width: 569, height: 911, device: "asus-zenpad-p024" },
    { name: "asus-landscape", width: 911, height: 569, device: "asus-zenpad-p024" },
  ];
}

function remoteDesktopCases() {
  return [
    [280, 280],
    [400, 720],
    [911, 569],
    [1_600, 280],
  ].map(([width, height]) => ({
    name: `linux-remote-${width}x${height}`,
    width,
    height,
    device: "linux-desktop",
  }));
}

function einkCases() {
  return [
    { name: "eink-narrow", width: 400, height: 600, device: "boox-go-color-7", eink: true, viewer: true },
    { name: "eink-short", width: 600, height: 400, device: "boox-go-color-7", eink: true, viewer: true },
    { name: "eink-portrait", width: 794, height: 1_054, device: "boox-go-color-7", eink: true, viewer: true },
    { name: "eink-landscape", width: 1_054, height: 700, device: "boox-go-color-7", eink: true, viewer: true },
  ];
}

async function openSurface(page, mode, scenario, { trust = "paired", viewer = scenario.viewer } = {}) {
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
    mode,
    source: "responsive-surface-test",
    preview: `${Date.now()}-${Math.random()}`,
  });
  if (scenario.device) {
    query.set("devicePreview", scenario.device);
    query.set("previewTrust", trust);
  }
  if (scenario.eink) query.set("eink", "1");
  if (viewer) query.set("viewer", "1");
  await page.goto(`${host}/index.html?${query}`, { waitUntil: "domcontentloaded" });
  await page.locator(`body.mode-${mode}`).waitFor();
  if (viewer) {
    const immersiveClass = mode === "display" ? "viewer-fullscreen" : "dashboard-viewer";
    await page.locator(`body.${immersiveClass}`).waitFor();
  }
  if (scenario.legacyContainerQueries) {
    await page.evaluate(() => {
      const removeContainerRules = owner => {
        const rules = owner.cssRules;
        for (let index = rules.length - 1; index >= 0; index -= 1) {
          const rule = rules[index];
          if (rule.constructor?.name === "CSSContainerRule") {
            owner.deleteRule(index);
            continue;
          }
          if (rule.styleSheet) {
            try { removeContainerRules(rule.styleSheet); } catch {}
          }
          if (rule.cssRules && typeof rule.deleteRule === "function") {
            try { removeContainerRules(rule); } catch {}
          }
        }
      };
      for (const sheet of document.styleSheets) {
        try { removeContainerRules(sheet); } catch {}
      }
    });
  }
}

async function prepareSurface(page, mode) {
  if (mode === "quota") {
    await page.waitForFunction(() => document.querySelector("#quotaGrid")?.children.length > 0);
    await page.evaluate(({ longCopy, longIdentity }) => {
      const email = document.querySelector(".quota-account-email");
      if (email) email.textContent = longIdentity;
      const status = document.querySelector(".quota-action-status");
      if (status) status.textContent = longCopy;
      const detail = document.querySelector(".quota-window small");
      if (detail) detail.textContent = longCopy;
    }, { longCopy, longIdentity });
  }

  if (mode === "display") {
    await page.evaluate(longCopy => {
      const toolbar = document.querySelector("#displayToolbar");
      const toggle = document.querySelector("#displayToolbarToggle");
      if (toolbar) {
        toolbar.hidden = false;
        toolbar.classList.remove("is-collapsed");
      }
      toggle?.setAttribute("aria-expanded", "true");
      const option = document.querySelector("#displaySource option:checked");
      if (option) option.textContent = longCopy;
      const emptyCopy = document.querySelector(".display-empty-state > span:not(.display-empty-icon)");
      if (emptyCopy) emptyCopy.textContent = longCopy;
    }, longCopy);
  }

  if (mode === "setup") {
    await page.evaluate(longCopy => {
      document.body.classList.add("pairing-active");
      const newDevice = document.querySelector("#newDeviceConnectPanel");
      if (newDevice) newDevice.open = true;
      document.querySelectorAll(".setup-panels-heading span, .new-device-panel summary small, .diagnostics-toggle small, #phonePairIntro")
        .forEach(element => { element.textContent = longCopy; });
      document.querySelectorAll(".connect-title span, .pairing-step span, .connect-qr span")
        .forEach(element => { element.textContent = `${longCopy} ${longCopy}`; });
    }, longCopy);
  }

  if (mode === "deck") {
    await page.evaluate(longCopy => {
      const copy = document.querySelector(".custom-deck-help-copy p");
      if (copy) copy.textContent = longCopy;
    }, longCopy);
  }
}

const auditConfig = {
  quota: {
    root: "#quotaView",
    structural: ["main", "#quotaView", ".quota-shell", ".quota-grid", ".quota-account-page", ".quota-account-card"],
    siblings: [".quota-shell", ".quota-grid", ".quota-account-card", ".quota-account-head", ".quota-footer", ".quota-codex-switch-row", ".quota-toolbox"],
  },
  display: {
    root: "#displayView",
    structural: ["main", "#displayView", ".display-toolbar", ".display-toolbar-extras", ".display-empty-state", ".display-stream-state"],
    siblings: [".display-toolbar", ".display-toolbar-extras", ".display-source-control", ".display-empty-state", ".display-empty-actions"],
  },
  setup: {
    root: "body",
    structural: ["body", "header", "main", ".phone-pair-rescue", ".setup-panels", ".new-device-panel", ".new-device-panel-content", ".connect-panel", ".connect-qr", ".connect-copy", ".pairing-guide", ".pairing-step", ".diagnostics-panel", ".diagnostics-panel-content"],
    siblings: ["header", "main", ".phone-pair-rescue", ".setup-panels", ".new-device-panel-content", ".connect-panel", ".connect-qr", ".connect-copy", ".pairing-guide", ".diagnostics-panel-content", ".diagnostics-actions"],
  },
  deck: {
    root: "#customDeckView",
    structural: ["main", "#customDeckView", ".custom-deck-help", ".custom-deck-steps", ".custom-deck-manifest"],
    siblings: [".custom-deck-help", ".custom-deck-steps"],
  },
  editor: {
    root: "#sideboardView",
    structural: ["main", "#sideboardView", "#sideboardShell", "#dashboardEditBar", "#systemSideboardPage"],
    siblings: ["#sideboardShell", "#dashboardEditBar", ".dashboard-edit-actions", "#systemSideboardPage"],
  },
  mobileDetail: {
    root: "#mobileDetailsPanel",
    structural: ["main", "#sideboardView", "#sideboardShell", "#mobileOverview", "#mobileDetailsPanel"],
    siblings: ["#mobileOverview", "#mobileDetailsPanel", ".mobile-detail-header", ".mobile-detail-metrics", ".mobile-detail-block"],
  },
  customManager: {
    root: "#customSideboardPage",
    selfContained: true,
    structural: ["main", "#sideboardView", "#sideboardShell", "#customSideboardPage", ".custom-sources-manager", ".custom-card-settings"],
    siblings: ["#customSideboardPage", ".custom-cards-top", ".custom-manager-header", ".custom-source-row", ".custom-source-form", ".custom-card-settings-form"],
  },
};

async function scrollReachableSurfaces(page, config) {
  await page.evaluate(({ root, structural, selfContained }) => {
    const rendered = element => {
      if (!element || element.hidden) return false;
      if (!element.getClientRects().length) return false;
      const closedDetails = element.closest("details:not([open])");
      if (closedDetails && element !== closedDetails && element !== closedDetails.querySelector(":scope > summary")) return false;
      const style = getComputedStyle(element);
      return style.display !== "none" && style.visibility !== "hidden";
    };
    const rootElement = document.querySelector(root);
    const elements = new Set(selfContained ? [rootElement].filter(Boolean) : [document.documentElement, document.body]);
    for (const selector of (selfContained ? structural : ["main", "header", root, ...structural])) {
      const scope = selfContained ? rootElement : document;
      scope?.querySelectorAll(selector).forEach(element => elements.add(element));
    }
    for (const element of elements) {
      if (!rendered(element)) continue;
      const style = getComputedStyle(element);
      if (["auto", "scroll"].includes(style.overflowY) && element.scrollHeight > element.clientHeight + 2) {
        element.scrollTop = element.scrollHeight;
      }
    }
    if (!selfContained) window.scrollTo(0, document.documentElement.scrollHeight);
  }, config);
  await page.waitForTimeout(30);
}

async function auditSurface(page, config) {
  return page.evaluate(({ root, structural, siblings }) => {
    const rendered = element => {
      if (!element || element.hidden) return false;
      if (!element.getClientRects().length) return false;
      const closedDetails = element.closest("details:not([open])");
      if (closedDetails && element !== closedDetails && element !== closedDetails.querySelector(":scope > summary")) return false;
      const style = getComputedStyle(element);
      return style.display !== "none" && style.visibility !== "hidden";
    };
    const visible = element => {
      if (!rendered(element)) return false;
      const rect = element.getBoundingClientRect();
      return rect.width > 0 && rect.height > 0;
    };
    const label = element => element.id || (typeof element.className === "string" ? element.className : "") || element.tagName;
    const unique = selectors => [...new Set(selectors.flatMap(selector => [...document.querySelectorAll(selector)]))];
    const nodes = unique(structural).filter(rendered);

    const zeroSize = nodes.filter(element => {
      const rect = element.getBoundingClientRect();
      return rect.width <= 0 || rect.height <= 0;
    }).map(label);

    const clipped = nodes.filter(element => {
      const style = getComputedStyle(element);
      return (
        (["hidden", "clip"].includes(style.overflowX) && element.scrollWidth > element.clientWidth + 2) ||
        (["hidden", "clip"].includes(style.overflowY) && element.scrollHeight > element.clientHeight + 2)
      );
    }).map(element => ({
      node: label(element),
      client: [element.clientWidth, element.clientHeight],
      scroll: [element.scrollWidth, element.scrollHeight],
      overflow: [getComputedStyle(element).overflowX, getComputedStyle(element).overflowY],
    }));

    const outsideX = nodes.filter(visible).filter(element => {
      const rect = element.getBoundingClientRect();
      return rect.left < -1 || rect.right > innerWidth + 1;
    }).map(element => {
      const rect = element.getBoundingClientRect();
      return { node: label(element), rect: [rect.left, rect.right], viewport: innerWidth };
    });

    const overlaps = [];
    for (const container of unique(siblings).filter(visible)) {
      const children = [...container.children].filter(visible).filter(element => {
        const style = getComputedStyle(element);
        return !["absolute", "fixed", "sticky"].includes(style.position) && style.display !== "contents";
      });
      for (let left = 0; left < children.length; left += 1) {
        for (let right = left + 1; right < children.length; right += 1) {
          const a = children[left].getBoundingClientRect();
          const b = children[right].getBoundingClientRect();
          const overlapX = Math.min(a.right, b.right) - Math.max(a.left, b.left);
          const overlapY = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
          if (overlapX > 2 && overlapY > 2) {
            overlaps.push({ container: label(container), pair: [label(children[left]), label(children[right])], area: [overlapX, overlapY] });
          }
        }
      }
    }

    const rootElement = document.querySelector(root);
    const candidates = rootElement ? [...rootElement.querySelectorAll("button, input, select, summary, a, p, li, pre, [role='status']")]
      .filter(visible)
      .filter(element => getComputedStyle(element).position !== "fixed") : [];
    const bottommost = candidates.sort((a, b) => b.getBoundingClientRect().bottom - a.getBoundingClientRect().bottom)[0];
    const bottomRect = bottommost?.getBoundingClientRect();
    const lastReachable = !bottomRect || (bottomRect.bottom <= innerHeight + 2 && bottomRect.bottom > 0);
    const scrollOwners = unique(["html", "body", "main", root, ...structural]).filter(rendered).map(element => ({
      node: label(element),
      client: [element.clientWidth, element.clientHeight],
      scroll: [element.scrollWidth, element.scrollHeight],
      offset: [element.scrollLeft, element.scrollTop],
      overflow: [getComputedStyle(element).overflowX, getComputedStyle(element).overflowY],
      position: getComputedStyle(element).position,
    }));

    return {
      documentOverflowX: document.documentElement.scrollWidth > innerWidth + 1,
      zeroSize,
      clipped,
      outsideX,
      overlaps,
      lastReachable,
      bottommost: bottommost ? { node: label(bottommost), rect: [bottomRect.top, bottomRect.bottom], text: bottommost.textContent?.trim().slice(0, 80) } : null,
      body: document.body.className,
      scrollOwners,
    };
  }, config);
}

async function expectHealthySurface(page, config) {
  let audit;
  // Live cards can finish an API render immediately after the first scroll.
  // Re-evaluate once from the new scroll extent; a genuinely unreachable
  // surface still fails identically on the second pass.
  for (let attempt = 0; attempt < 2; attempt += 1) {
    await scrollReachableSurfaces(page, config);
    audit = await auditSurface(page, config);
    if (audit.lastReachable) break;
    await page.waitForTimeout(50);
  }
  const detail = JSON.stringify(audit, null, 2);
  expect(audit.documentOverflowX, detail).toBe(false);
  expect(audit.zeroSize, detail).toEqual([]);
  expect(audit.clipped, detail).toEqual([]);
  expect(audit.outsideX, detail).toEqual([]);
  expect(audit.overlaps, detail).toEqual([]);
  expect(audit.lastReachable, detail).toBe(true);
}

test.describe("Quota across continuous and unusual viewports", () => {
  for (const scenario of [...pcCases(), ...remoteDesktopCases(), ...browserDeviceCases(), ...einkCases()]) {
    test(scenario.name, async ({ page }) => {
      await openSurface(page, "quota", scenario);
      await prepareSurface(page, "quota");
      await expectHealthySurface(page, auditConfig.quota);
    });
  }
});

test.describe("Display controls follow available space", () => {
  for (const scenario of [...pcCases(), ...remoteDesktopCases(), ...browserDeviceCases()]) {
    test(scenario.name, async ({ page }) => {
      await openSurface(page, "display", scenario);
      await prepareSurface(page, "display");
      await expectHealthySurface(page, auditConfig.display);
    });
  }
});

test.describe("Setup remains one reachable document", () => {
  const cases = [
    ...pcCases(),
    ...remoteDesktopCases().map(scenario => ({ ...scenario, name: `${scenario.name}-unpaired`, trust: "unpaired" })),
    ...browserDeviceCases().map(scenario => ({ ...scenario, name: `${scenario.name}-unpaired`, trust: "unpaired" })),
  ];
  for (const scenario of cases) {
    test(scenario.name, async ({ page }) => {
      await openSurface(page, "setup", scenario, { trust: scenario.trust || "local" });
      await prepareSurface(page, "setup");
      await expectHealthySurface(page, auditConfig.setup);
    });
  }
});

test.describe("Custom Deck help uses its own viewport", () => {
  for (const scenario of [...pcCases(), ...browserDeviceCases()]) {
    test(scenario.name, async ({ page }) => {
      await openSurface(page, "deck", scenario);
      await prepareSurface(page, "deck");
      await expectHealthySurface(page, auditConfig.deck);
    });
  }
});

test.describe("Sideboard editing and management stay scrollable", () => {
  const cases = [
    { name: "pc-mid", width: 760, height: 420 },
    { name: "pc-short", width: 1_600, height: 280 },
    { name: "asus-landscape", width: 911, height: 569, device: "asus-zenpad-p024", viewer: true },
    { name: "eink-narrow", width: 400, height: 600, device: "boox-go-color-7", eink: true, viewer: true },
    { name: "eink-portrait", width: 794, height: 1_054, device: "boox-go-color-7", eink: true, viewer: true },
  ];
  for (const scenario of cases) {
    test(`${scenario.name}-editor`, async ({ page }) => {
      await openSurface(page, "sideboard", scenario);
      await page.locator("#dashboardEditToggle").click();
      await page.locator("body.dashboard-edit-mode").waitFor();
      await expectHealthySurface(page, auditConfig.editor);
    });

  }

  const managerCases = [
    { name: "pc-mid", width: 760, height: 420 },
    { name: "pc-short", width: 1_600, height: 280 },
    { name: "pc-resized-narrow", width: 280, height: 568, openWidth: 760, openHeight: 600 },
  ];
  for (const scenario of managerCases) {
    test(`${scenario.name}-manager`, async ({ page }) => {
      await openSurface(page, "sideboard", {
        ...scenario,
        width: scenario.openWidth || scenario.width,
        height: scenario.openHeight || scenario.height,
      });
      await page.locator("#dashboardEditToggle").click();
      await page.locator("#customManageButton").click();
      await page.locator("#customSideboardPage:not([hidden])").waitFor();
      await page.locator("#customManagerAdd").click();
      await page.locator("#customSourceForm:not([hidden])").waitFor();
      if (scenario.openWidth) await page.setViewportSize({ width: scenario.width, height: scenario.height });
      await expectHealthySurface(page, auditConfig.customManager);
    });
  }
});

test.describe("Compact Sideboard detail panels", () => {
  const cases = [
    { name: "micro", width: 280, height: 280, device: "galaxy-s23" },
    { name: "phone", width: 320, height: 568, device: "galaxy-s23" },
    { name: "short", width: 667, height: 280, device: "galaxy-s23" },
    { name: "iphone", width: 375, height: 812, device: "iphone-xs" },
  ];
  for (const scenario of cases) {
    test(scenario.name, async ({ page }) => {
      await openSurface(page, "sideboard", scenario);
      await page.locator("#mobileDetailsOpen").click();
      await page.locator("#mobileDetailsPanel:not([hidden])").waitFor();
      await expectHealthySurface(page, auditConfig.mobileDetail);
    });
  }
});

test.describe("Sideboard cards contain unbounded live content", () => {
  for (const scenario of [
    { name: "wide-short-windowed", width: 1_804, height: 754, viewer: false },
    { name: "wide-short-fullscreen", width: 1_804, height: 754, viewer: true },
    { name: "linux-wide-windowed", width: 1_804, height: 754, device: "linux-desktop", viewer: false },
    { name: "linux-wide-fullscreen", width: 1_804, height: 754, device: "linux-desktop", viewer: true },
    { name: "wide-shallow", width: 1_600, height: 400, viewer: true },
    { name: "mid-landscape", width: 911, height: 569, device: "asus-zenpad-p024", viewer: true },
    { name: "chrome101-mid-windowed", width: 911, height: 569, device: "asus-zenpad-p024", viewer: false, legacyContainerQueries: true },
    { name: "chrome101-mid-fullscreen", width: 911, height: 569, device: "asus-zenpad-p024", viewer: true, legacyContainerQueries: true },
  ]) {
    test(scenario.name, async ({ page }) => {
      await openSurface(page, "sideboard", scenario);
      await page.locator("#systemSideboardPage.dashboard-grid").waitFor();
      await page.evaluate(longCopy => {
        const list = document.querySelector(".activity-feed-list");
        if (!list) throw new Error("Activity Feed list is missing.");
        list.replaceChildren(...Array.from({ length: 40 }, (_, index) => {
          const item = document.createElement("li");
          item.className = "activity-feed-item";
          item.innerHTML = `<div class="activity-feed-meta"><b class="activity-source">Windows notification</b><span>Stress source ${index + 1}</span><time>09:${String(index).padStart(2, "0")}</time></div><div class="activity-feed-text"></div>`;
          item.querySelector(".activity-feed-text").textContent = `${longCopy} ${longCopy}`;
          return item;
        }));
      }, longCopy);

      const metrics = await page.evaluate(() => {
        const grid = document.querySelector("#systemSideboardPage.dashboard-grid");
        const card = document.querySelector(".activity-feed-card");
        const list = document.querySelector(".activity-feed-list");
        const heading = document.querySelector(".activity-feed-head");
        const shell = document.querySelector("#sideboardShell");
        const view = document.querySelector("#sideboardView");
        const main = document.querySelector("main");
        const gridRect = grid.getBoundingClientRect();
        const cardRect = card.getBoundingClientRect();
        const headingRect = heading.getBoundingClientRect();
        return {
          body: document.body.className,
          grid: [grid.clientHeight, grid.scrollHeight],
          main: [main.clientHeight, main.scrollHeight, getComputedStyle(main).height, getComputedStyle(main).overflowY],
          view: [view.clientHeight, view.scrollHeight, getComputedStyle(view).height, getComputedStyle(view).overflowY],
          viewWidth: view.clientWidth,
          shell: [shell.clientHeight, shell.scrollHeight, getComputedStyle(shell).height, getComputedStyle(shell).overflowY],
          card: [card.clientHeight, card.scrollHeight, getComputedStyle(card).overflowY],
          list: [list.clientHeight, list.scrollHeight, getComputedStyle(list).overflowY],
          viewportHeight: window.innerHeight,
          sideboardSpace: [...view.classList].filter(name => name.startsWith("space-sideboard-")),
          cardInsideGrid: cardRect.top >= gridRect.top - 1 && cardRect.bottom <= gridRect.bottom + 1,
          headingInsideCard: headingRect.top >= cardRect.top - 1 && headingRect.bottom <= cardRect.bottom + 1,
        };
      });
      const detail = JSON.stringify(metrics, null, 2);
      expect(metrics.grid[1], detail).toBeLessThanOrEqual(metrics.grid[0] + 2);
      expect(metrics.card[2], detail).toBe("hidden");
      expect(metrics.list[2], detail).toBe("auto");
      expect(metrics.list[0], detail).toBeGreaterThan(0);
      expect(metrics.list[1], detail).toBeGreaterThan(metrics.list[0] + 2);
      const expectedSpace = metrics.viewWidth <= 44 * 16
        ? "space-sideboard-compact"
        : metrics.viewWidth <= 64 * 16
          ? "space-sideboard-mid"
          : "space-sideboard-wide";
      expect(metrics.sideboardSpace, detail).toContain(expectedSpace);
      expect(metrics.main[1], detail).toBeLessThanOrEqual(metrics.main[0] + 2);
      expect(metrics.view[1], detail).toBeLessThanOrEqual(metrics.view[0] + 2);
      expect(metrics.shell[1], detail).toBeLessThanOrEqual(metrics.shell[0] + 6);
      expect(metrics.cardInsideGrid, detail).toBe(true);
      expect(metrics.headingInsideCard, detail).toBe(true);
    });
  }
});

test("Chrome 101 compact fallback uses the mobile overview without container queries", async ({ page }) => {
  await openSurface(page, "sideboard", {
    name: "chrome101-compact",
    width: 569,
    height: 911,
    device: "asus-zenpad-p024",
    legacyContainerQueries: true,
  });
  const state = await page.evaluate(() => ({
    classes: [...document.querySelector("#sideboardView").classList],
    overview: getComputedStyle(document.querySelector("#mobileOverview")).display,
    grid: getComputedStyle(document.querySelector("#systemSideboardPage")).display,
    body: [document.body.clientHeight, document.body.scrollHeight],
  }));
  const detail = JSON.stringify(state, null, 2);
  expect(state.classes, detail).toContain("space-sideboard-compact");
  expect(state.overview, detail).toBe("flex");
  expect(state.grid, detail).toBe("none");
  expect(state.body[1], detail).toBeLessThanOrEqual(state.body[0] + 2);
});

test("ZenPad fullscreen stream is centered exactly once", async ({ page }) => {
  await openSurface(page, "display", {
    name: "zenpad-fullscreen-stream",
    width: 962,
    height: 602,
    device: "asus-zenpad-p024",
    viewer: true,
  });
  const geometry = await page.evaluate(() => {
    const video = document.querySelector("#rtcScreen");
    video.hidden = false;
    document.querySelector("#screen").hidden = true;
    document.body.classList.add("stream-online");
    const rect = video.getBoundingClientRect();
    return {
      viewport: [innerWidth, innerHeight],
      rect: [rect.left, rect.top, rect.right, rect.bottom, rect.width, rect.height],
      style: {
        inset: [getComputedStyle(video).top, getComputedStyle(video).right, getComputedStyle(video).bottom, getComputedStyle(video).left],
        margin: getComputedStyle(video).margin,
        transform: getComputedStyle(video).transform,
      },
    };
  });
  const detail = JSON.stringify(geometry, null, 2);
  expect(Math.abs(geometry.rect[0]), detail).toBeLessThanOrEqual(1);
  expect(Math.abs(geometry.rect[1]), detail).toBeLessThanOrEqual(1);
  expect(Math.abs(geometry.rect[2] - geometry.viewport[0]), detail).toBeLessThanOrEqual(1);
  expect(Math.abs(geometry.rect[3] - geometry.viewport[1]), detail).toBeLessThanOrEqual(1);
});
