import assert from "node:assert/strict";
import test from "node:test";

import { buildKeepAwakeView } from "../../src/VibeDeck.Host/wwwroot/modules/keep-awake-controller.js";

test("keep-awake view distinguishes disabled, active and unavailable states", () => {
  assert.deepEqual(buildKeepAwakeView({ desired: false, hasWakeLock: false, videoPlaying: false, ios: false, protocol: "http:" }), {
    text: "長亮：關", good: false, buttonText: "長亮 OFF", active: false,
  });
  assert.equal(buildKeepAwakeView({ desired: true, hasWakeLock: true, videoPlaying: false, ios: false, protocol: "https:" }).active, true);
  assert.equal(buildKeepAwakeView({ desired: true, hasWakeLock: false, videoPlaying: true, ios: true, protocol: "http:" }).text, "長亮：開");
  assert.equal(buildKeepAwakeView({ desired: true, hasWakeLock: false, videoPlaying: false, ios: true, protocol: "http:" }).text, "長亮：需 HTTPS");
});

test("keep-awake idle enabled state prompts user gesture", () => {
  const view = buildKeepAwakeView({ desired: true, hasWakeLock: false, videoPlaying: false, ios: false, protocol: "https:" });
  assert.equal(view.text, "長亮：點按鈕開啟");
  assert.equal(view.buttonText, "長亮 ON");
  assert.equal(view.active, false);
});
