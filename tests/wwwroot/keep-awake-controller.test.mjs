import assert from "node:assert/strict";
import test from "node:test";

import { buildKeepAwakeView, createKeepAwakeController } from "../../src/VibeDeck.Host/wwwroot/modules/keep-awake-controller.js";
import { readFile } from "node:fs/promises";

test("keep-awake view reports automatic active and unavailable states", () => {
  assert.deepEqual(buildKeepAwakeView({ hasWakeLock: true, videoPlaying: false, ios: false, protocol: "https:" }), {
    text: "長亮：開", good: true,
  });
  assert.equal(buildKeepAwakeView({ hasWakeLock: false, videoPlaying: true, ios: true, protocol: "http:" }).text, "長亮：開");
  assert.equal(buildKeepAwakeView({ hasWakeLock: false, videoPlaying: false, ios: true, protocol: "http:" }).text, "長亮：需 HTTPS");
});

test("keep-awake idle state waits for a generic user gesture without a dedicated toggle", () => {
  const view = buildKeepAwakeView({ hasWakeLock: false, videoPlaying: false, ios: false, protocol: "https:" });
  assert.equal(view.text, "長亮：等待互動");
  assert.equal(view.good, false);
});

test("keep-awake is automatic and has no dedicated header toggle", async () => {
  const [html, index, controller] = await Promise.all([
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.html", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/keep-awake-controller.js", import.meta.url), "utf8"),
  ]);
  assert.doesNotMatch(html, /id="keepAwake"/);
  assert.doesNotMatch(index, /keepAwakeButton|keepAwakeController\.toggle/);
  assert.doesNotMatch(controller, /vibeDeckKeepAwake|setDesired|async function toggle/);
  assert.match(controller, /function isDesired\(\)\s*\{\s*return true;/);
});

test("mobile pointer gesture reuses the original ensure path", async () => {
  let requestCalls = 0;
  let playCalls = 0;
  const video = {
    paused: true,
    muted: false,
    defaultMuted: false,
    playsInline: false,
    loop: false,
    currentTime: 0,
    setAttribute() {},
    pause() { this.paused = true; },
    async play() {
      playCalls += 1;
      this.paused = false;
    },
  };
  const controller = createKeepAwakeController({
    document: { visibilityState: "visible" },
    window: { isSecureContext: true, location: { protocol: "https:" } },
    navigator: {
      wakeLock: {
        async request() {
          requestCalls += 1;
          return { addEventListener() {}, async release() {} };
        },
      },
    },
    video,
    isIos: () => true,
    isMobileClient: () => true,
    setWakeState() {},
  });

  controller.handlePointerDown();
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(requestCalls, 1);
  assert.equal(playCalls, 0, "successful native Wake Lock should not start the media fallback");
});

test("mobile pointer gesture falls back to the original silent video path when Wake Lock is unavailable", async () => {
  let playCalls = 0;
  const video = {
    paused: true,
    muted: false,
    defaultMuted: false,
    playsInline: false,
    loop: false,
    currentTime: 0,
    setAttribute() {},
    pause() { this.paused = true; },
    async play() {
      playCalls += 1;
      this.paused = false;
    },
  };
  const controller = createKeepAwakeController({
    document: { visibilityState: "visible" },
    window: { isSecureContext: true, location: { protocol: "https:" } },
    navigator: {},
    video,
    isIos: () => true,
    isMobileClient: () => true,
    setWakeState() {},
  });

  controller.handlePointerDown();
  await new Promise(resolve => setTimeout(resolve, 0));
  assert.equal(playCalls, 1);
});

test("automatic keep-awake keeps the proven controller flow without the old toggle state", async () => {
  const controller = await readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/keep-awake-controller.js", import.meta.url), "utf8");
  assert.doesNotMatch(controller, /requestWakeLock|wakeLockRequest|Promise\.allSettled/);
  assert.match(controller, /setInterval\([\s\S]*15000\)/);
  assert.match(controller, /function handlePointerDown\(\)[\s\S]*ensure\(\)/);
});
