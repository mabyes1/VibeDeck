import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_APP_THEME,
  buildAppThemeVars,
  createAppThemeController,
  normalizeAppTheme,
} from "../../src/VibeDeck.Host/wwwroot/modules/app-theme.js";

function fakeStorage(values = {}) {
  const data = new Map(Object.entries(values));
  return {
    getItem: key => data.has(key) ? data.get(key) : null,
    setItem: (key, value) => data.set(key, String(value)),
    removeItem: key => data.delete(key),
  };
}

function fakeTarget() {
  const values = new Map();
  return {
    dataset: {},
    style: {
      setProperty: (name, value) => values.set(name, value),
      getPropertyValue: name => values.get(name) || "",
    },
  };
}

const fakeRoot = {
  getElementById: () => null,
  querySelectorAll: () => [],
};

test("app theme normalizes colors and clamps tuneable values", () => {
  const theme = normalizeAppTheme({
    colorA: "#abc",
    colorB: "not-a-color",
    angle: 999,
    intensity: 5,
  });

  assert.equal(theme.colorA, "#aabbcc");
  assert.equal(theme.colorB, DEFAULT_APP_THEME.colorB);
  assert.equal(theme.angle, 360);
  assert.equal(theme.intensity, 30);
  assert.equal(theme.backgroundMode, "gradient");
  assert.equal(theme.glassOpacity, DEFAULT_APP_THEME.glassOpacity);
});

test("app theme clamps background and neutral glass controls", () => {
  const theme = normalizeAppTheme({
    backgroundMode: "image",
    backgroundFit: "contain",
    backgroundPosition: "bottom",
    backgroundBlur: 99,
    backgroundDim: -1,
    glassOpacity: 99,
    glassBlur: -4,
    glassBorder: 1,
    backgroundImage: "data:image/webp;base64,abc",
  });

  assert.equal(theme.backgroundMode, "image");
  assert.equal(theme.backgroundFit, "contain");
  assert.equal(theme.backgroundPosition, "bottom");
  assert.equal(theme.backgroundBlur, 30);
  assert.equal(theme.backgroundDim, 0);
  assert.equal(theme.glassOpacity, 20);
  assert.equal(theme.glassBlur, 0);
  assert.equal(theme.glassBorder, 4);
  assert.equal(theme.backgroundImage, "data:image/webp;base64,abc");
});

test("app theme derives reusable palette and accent tokens", () => {
  const vars = buildAppThemeVars({
    colorA: "#4b1f66",
    colorB: "#17344d",
    angle: 132,
    intensity: 72,
  });

  assert.equal(vars["--theme-palette-a"], "#4b1f66");
  assert.equal(vars["--theme-palette-b"], "#17344d");
  assert.equal(vars["--theme-gradient-angle"], "132deg");
  assert.match(vars["--theme-palette-a-soft"], /^rgba\(/);
  assert.match(vars["--theme-accent"], /^#[0-9a-f]{6}$/);
  assert.match(vars["--theme-accent-2"], /^#[0-9a-f]{6}$/);
  assert.equal(vars["--theme-background-dim"], "0.240");
  assert.equal(vars["--theme-glass-blur"], "20px");
  assert.match(vars["--theme-glass"], /^rgba\(255, 255, 255,/);
});

test("host theme overrides browser-local appearance and supplies the shared background URL", async () => {
  const calls = [];
  const target = fakeTarget();
  const controller = createAppThemeController({
    target,
    root: fakeRoot,
    storage: fakeStorage({
      vibeDeckAppThemeV1: JSON.stringify({ ...DEFAULT_APP_THEME, colorA: "#ff0000" }),
    }),
    requestJson: async (url, init) => {
      calls.push([url, init?.method || "GET"]);
      return {
        configured: true,
        hasBackgroundImage: true,
        backgroundUrl: "/api/appearance/background?v=123",
        theme: { ...DEFAULT_APP_THEME, backgroundMode: "image", colorA: "#123456" },
      };
    },
  });

  await controller.loadFromHost();

  assert.equal(controller.getTheme().colorA, "#123456");
  assert.equal(controller.getTheme().backgroundImage, "/api/appearance/background?v=123");
  assert.equal(target.dataset.themeBackground, "image");
  assert.deepEqual(calls, [["/api/appearance/theme", "GET"]]);
});

test("an unconfigured host is not claimed by a client that only has default appearance", async () => {
  const calls = [];
  const controller = createAppThemeController({
    target: fakeTarget(),
    root: fakeRoot,
    storage: fakeStorage(),
    requestJson: async (url, init) => {
      calls.push([url, init?.method || "GET"]);
      return { configured: false, hasBackgroundImage: false, backgroundUrl: "", theme: DEFAULT_APP_THEME };
    },
  });

  await controller.loadFromHost();

  assert.deepEqual(calls, [["/api/appearance/theme", "GET"]]);
});

test("an unconfigured host migrates a meaningful legacy browser theme once", async () => {
  const calls = [];
  const controller = createAppThemeController({
    target: fakeTarget(),
    root: fakeRoot,
    storage: fakeStorage({
      vibeDeckAppThemeV1: JSON.stringify({ ...DEFAULT_APP_THEME, colorA: "#abcdef" }),
    }),
    requestJson: async (url, init) => {
      calls.push([url, init?.method || "GET"]);
      if (!init) return { configured: false, hasBackgroundImage: false, backgroundUrl: "", theme: DEFAULT_APP_THEME };
      return { configured: true, hasBackgroundImage: false, backgroundUrl: "", theme: { ...DEFAULT_APP_THEME, colorA: "#abcdef" } };
    },
  });

  await controller.loadFromHost();

  assert.deepEqual(calls, [
    ["/api/appearance/theme", "GET"],
    ["/api/appearance/theme", "PUT"],
  ]);
  assert.equal(controller.getTheme().colorA, "#abcdef");
});
