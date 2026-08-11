import assert from "node:assert/strict";
import test from "node:test";

import {
  chooseAutoDisplayMode,
  computeClientDisplayTarget,
} from "../../src/VibeDeck.Host/wwwroot/modules/display-auto-mode.js";

test("auto target caps phone DPR instead of streaming native 3x pixels", () => {
  assert.deepEqual(computeClientDisplayTarget({
    screenWidth: 780,
    screenHeight: 360,
    viewportWidth: 780,
    viewportHeight: 360,
    devicePixelRatio: 3,
  }), { width: 1170, height: 540 });
});

test("auto target maps a density-scaled 16:10 tablet near 1280x800", () => {
  assert.deepEqual(computeClientDisplayTarget({
    screenWidth: 960,
    screenHeight: 600,
    viewportWidth: 960,
    viewportHeight: 600,
    devicePixelRatio: 4 / 3,
  }), { width: 1280, height: 800 });
});

test("auto target follows current orientation without using browser toolbar height as aspect", () => {
  assert.deepEqual(computeClientDisplayTarget({
    screenWidth: 960,
    screenHeight: 600,
    viewportWidth: 590,
    viewportHeight: 900,
    devicePixelRatio: 4 / 3,
  }), { width: 800, height: 1280 });
});

test("auto mode strongly prefers aspect ratio before small size differences", () => {
  const result = chooseAutoDisplayMode([
    { Id: "16-9", Width: 1366, Height: 768 },
    { Id: "16-10", Width: 1280, Height: 800 },
    { Id: "4-3", Width: 1280, Height: 960 },
  ], {
    screenWidth: 960,
    screenHeight: 600,
    viewportWidth: 960,
    viewportHeight: 600,
    devicePixelRatio: 4 / 3,
  });

  assert.equal(result.preset.Id, "16-10");
  assert.deepEqual(result.target, { width: 1280, height: 800 });
});
