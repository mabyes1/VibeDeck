import test from "node:test";
import assert from "node:assert/strict";
import {
  AUTO_WEBRTC_COOLDOWN_MS,
  PREFER_WEBRTC_COOLDOWN_MS,
  createWebRtcRetryScheduler,
  webRtcCooldownMs,
} from "../../src/VibeDeck.Host/wwwroot/modules/webrtc-retry-scheduler.js";
import { createStreamController } from "../../src/VibeDeck.Host/wwwroot/modules/stream-controller.js";

function createFakeClock() {
  let current = 0;
  let nextId = 1;
  const timers = new Map();

  function setTimer(callback, delay) {
    const id = nextId++;
    timers.set(id, {
      callback,
      due: current + Math.max(0, Number(delay) || 0),
    });
    return id;
  }

  function clearTimer(id) {
    timers.delete(id);
  }

  function nextEntry() {
    return [...timers.entries()]
      .map(([id, value]) => ({ id, ...value }))
      .sort((left, right) => left.due - right.due || left.id - right.id)[0] || null;
  }

  function advance(milliseconds) {
    const target = current + milliseconds;
    while (true) {
      const next = nextEntry();
      if (!next || next.due > target) break;
      current = next.due;
      timers.delete(next.id);
      next.callback();
    }
    current = target;
  }

  function takeNextCallback() {
    const next = nextEntry();
    if (!next) return null;
    timers.delete(next.id);
    return next.callback;
  }

  return {
    now: () => current,
    setTimer,
    clearTimer,
    advance,
    takeNextCallback,
    pendingCount: () => timers.size,
  };
}

function createHarness({ mode = "auto", prefersWebRtc = true, generation = 1 } = {}) {
  const clock = createFakeClock();
  let currentMode = mode;
  let currentGeneration = generation;
  let retries = 0;
  const scheduler = createWebRtcRetryScheduler({
    getGeneration: () => currentGeneration,
    getTransportMode: () => currentMode,
    prefersWebRtcDisplay: () => prefersWebRtc,
    retry: () => { retries += 1; },
    now: clock.now,
    setTimer: clock.setTimer,
    clearTimer: clock.clearTimer,
  });

  return {
    clock,
    scheduler,
    retries: () => retries,
    setMode: value => { currentMode = value; },
    setGeneration: value => { currentGeneration = value; },
  };
}

function installGlobal(name, value) {
  const existed = Object.prototype.hasOwnProperty.call(globalThis, name);
  const previous = globalThis[name];
  globalThis[name] = value;
  return () => {
    if (existed) globalThis[name] = previous;
    else delete globalThis[name];
  };
}

function createControllerHarness(initialMode = "auto") {
  const clock = createFakeClock();
  let mode = initialMode;
  let offerAttempts = 0;

  class FakePeerConnection {
    constructor() {
      this.connectionState = "new";
      this.iceConnectionState = "new";
      this.iceGatheringState = "complete";
      this.signalingState = "stable";
      this.localDescription = null;
    }

    addTransceiver() {}
    addEventListener() {}
    removeEventListener() {}

    async createOffer() {
      return { type: "offer", sdp: "fake-offer" };
    }

    async setLocalDescription(offer) {
      this.localDescription = offer;
    }

    async setRemoteDescription() {}

    close() {
      this.connectionState = "closed";
    }
  }

  class FakeWebSocket {
    constructor(url) {
      this.url = url;
      this.binaryType = "";
    }

    close() {
      this.onclose?.();
    }
  }

  const restoreWindow = installGlobal("window", {
    isSecureContext: true,
    RTCPeerConnection: FakePeerConnection,
  });
  const restorePeer = installGlobal("RTCPeerConnection", FakePeerConnection);
  const restoreWebSocket = installGlobal("WebSocket", FakeWebSocket);
  const originalWarn = console.warn;
  console.warn = () => {};

  const elements = {
    screen: {
      hidden: false,
      addEventListener() {},
      removeEventListener() {},
    },
    rtcScreen: {
      hidden: true,
      srcObject: null,
      play: async () => {},
    },
  };
  const controller = createStreamController({
    elements,
    getWsBase: () => "ws://test",
    appendDeviceToken: parameters => parameters,
    getSelectedDisplayName: () => "DISPLAY1",
    getStreamSettings: () => ({
      fps: 30,
      quality: 48,
      transportMode: mode,
      rotationIsAuto: false,
    }),
    canUseProtectedConnection: () => true,
    loadPhoneDisplay: async () => {},
    prefersWebRtcDisplay: () => true,
    isLoopbackHost: () => false,
    setStatus: () => {},
    applyRotation: () => {},
    resetJpegStats: () => {},
    recordJpegFrame: () => {},
    fetchJsonOrThrow: async path => {
      if (path === "/api/stream/ice") {
        return { iceServers: [], turnAvailable: false, warning: "" };
      }
      if (path === "/api/stream/webrtc/offer") {
        offerAttempts += 1;
        throw new Error("Host offline HTML response");
      }
      throw new Error(`Unexpected request: ${path}`);
    },
    tuneVideoReceiver: () => {},
    createRetryScheduler: options => createWebRtcRetryScheduler({
      ...options,
      now: clock.now,
      setTimer: clock.setTimer,
      clearTimer: clock.clearTimer,
    }),
    getNow: clock.now,
  });

  return {
    clock,
    controller,
    offerAttempts: () => offerAttempts,
    setMode: value => { mode = value; },
    restore: () => {
      console.warn = originalWarn;
      restoreWebSocket();
      restorePeer();
      restoreWindow();
    },
  };
}

async function flushMicrotasks() {
  for (let index = 0; index < 12; index += 1) {
    await Promise.resolve();
  }
}

test("cooldown policy keeps AUTO at 120s and prefer-webrtc at 30s", () => {
  assert.equal(webRtcCooldownMs("auto"), AUTO_WEBRTC_COOLDOWN_MS);
  assert.equal(AUTO_WEBRTC_COOLDOWN_MS, 120000);
  assert.equal(webRtcCooldownMs("webrtc"), PREFER_WEBRTC_COOLDOWN_MS);
  assert.equal(PREFER_WEBRTC_COOLDOWN_MS, 30000);
});

test("AUTO fallback retries once when its cooldown expires", () => {
  const harness = createHarness();
  assert.equal(harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS), true);
  assert.equal(harness.clock.pendingCount(), 1);

  harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS - 1);
  assert.equal(harness.retries(), 0);
  harness.clock.advance(1);

  assert.equal(harness.retries(), 1);
  assert.equal(harness.clock.pendingCount(), 0);
});

test("prefer-webrtc fallback uses the shorter cooldown", () => {
  const harness = createHarness({ mode: "webrtc" });
  harness.scheduler.schedule(1, PREFER_WEBRTC_COOLDOWN_MS);

  harness.clock.advance(PREFER_WEBRTC_COOLDOWN_MS);

  assert.equal(harness.retries(), 1);
});

test("rescheduling replaces the prior timer and stale callback", () => {
  const harness = createHarness();
  harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS);
  const staleCallback = harness.clock.takeNextCallback();

  harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS + 1000);
  assert.equal(harness.clock.pendingCount(), 1);
  staleCallback();
  assert.equal(harness.retries(), 0);
  assert.equal(harness.clock.pendingCount(), 1);

  harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS + 1000);
  assert.equal(harness.retries(), 1);
});

test("manual JPEG mode suppresses a pending retry", () => {
  const harness = createHarness();
  harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS);
  harness.setMode("jpeg");

  harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS);

  assert.equal(harness.retries(), 0);
  assert.equal(harness.clock.pendingCount(), 0);
});

test("a newer connection generation makes the old timer inert", () => {
  const harness = createHarness();
  harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS);
  harness.setGeneration(2);

  harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS);

  assert.equal(harness.retries(), 0);
});

test("stop cancellation protects against an already queued callback", () => {
  const harness = createHarness();
  harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS);
  const queuedCallback = harness.clock.takeNextCallback();

  harness.scheduler.cancel();
  queuedCallback();

  assert.equal(harness.retries(), 0);
  assert.equal(harness.scheduler.hasScheduledRetry(), false);
});

test("an early timer callback rearms the original deadline", () => {
  const harness = createHarness();
  harness.scheduler.schedule(1, AUTO_WEBRTC_COOLDOWN_MS);
  const earlyCallback = harness.clock.takeNextCallback();

  earlyCallback();
  assert.equal(harness.retries(), 0);
  assert.equal(harness.clock.pendingCount(), 1);

  harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS);
  assert.equal(harness.retries(), 1);
});

test("controller retries an offline offer after AUTO cooldown and stop cancels the next retry", async () => {
  const harness = createControllerHarness("auto");
  try {
    await harness.controller.connect();
    assert.equal(harness.offerAttempts(), 1);
    assert.equal(harness.clock.pendingCount(), 1);

    harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS - 1);
    await flushMicrotasks();
    assert.equal(harness.offerAttempts(), 1);

    harness.clock.advance(1);
    await flushMicrotasks();
    assert.equal(harness.offerAttempts(), 2);
    assert.equal(harness.clock.pendingCount(), 1);

    harness.controller.closeJpegStream();
    harness.controller.closeRtcStream();
    assert.equal(harness.clock.pendingCount(), 0);
    harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS);
    await flushMicrotasks();
    assert.equal(harness.offerAttempts(), 2);
  } finally {
    harness.restore();
  }
});

test("controller retry cannot override a manual JPEG selection", async () => {
  const harness = createControllerHarness("auto");
  try {
    await harness.controller.connect();
    harness.setMode("jpeg");
    harness.clock.advance(AUTO_WEBRTC_COOLDOWN_MS);
    await flushMicrotasks();

    assert.equal(harness.offerAttempts(), 1);
    assert.equal(harness.clock.pendingCount(), 0);
  } finally {
    harness.controller.closeJpegStream();
    harness.controller.closeRtcStream();
    harness.restore();
  }
});

test("controller new generation makes the old cooldown callback inert", async () => {
  const harness = createControllerHarness("auto");
  try {
    await harness.controller.connect();
    const staleCallback = harness.clock.takeNextCallback();

    await harness.controller.connect();
    assert.equal(harness.offerAttempts(), 1);
    assert.equal(harness.clock.pendingCount(), 1);

    staleCallback();
    await flushMicrotasks();
    assert.equal(harness.offerAttempts(), 1);
    assert.equal(harness.clock.pendingCount(), 1);
  } finally {
    harness.controller.closeJpegStream();
    harness.controller.closeRtcStream();
    harness.restore();
  }
});

test("controller prefer-webrtc mode retries after 30 seconds", async () => {
  const harness = createControllerHarness("webrtc");
  try {
    await harness.controller.connect();
    harness.clock.advance(PREFER_WEBRTC_COOLDOWN_MS - 1);
    await flushMicrotasks();
    assert.equal(harness.offerAttempts(), 1);

    harness.clock.advance(1);
    await flushMicrotasks();
    assert.equal(harness.offerAttempts(), 2);
  } finally {
    harness.controller.closeJpegStream();
    harness.controller.closeRtcStream();
    harness.restore();
  }
});
