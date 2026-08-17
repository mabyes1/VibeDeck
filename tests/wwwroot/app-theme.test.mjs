import assert from "node:assert/strict";
import test from "node:test";

import {
  DEFAULT_APP_THEME,
  buildAppThemeVars,
  normalizeAppTheme,
} from "../../src/VibeDeck.Host/wwwroot/modules/app-theme.js";

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
