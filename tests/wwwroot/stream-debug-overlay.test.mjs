import assert from "node:assert/strict";
import test from "node:test";

import {
  buildStreamDebugView,
  classifyStreamBottleneck,
} from "../../src/VibeDeck.Host/wwwroot/modules/stream-debug-overlay.js";

test("stream debug classifies pacing debt before downstream symptoms", () => {
  const result = classifyStreamBottleneck({
    client: { fps: 18, rttMs: 8, bufferMs: 10, decodeMs: 3 },
    host: { TargetFps: 18, RecentQueuedFps: 18 },
    transport: { PacingDebtMs: 350 },
  });

  assert.equal(result.code, "pacer");
});

test("stream debug classifies host frame starvation", () => {
  const result = classifyStreamBottleneck({
    client: { fps: 8, rttMs: 8, bufferMs: 10, decodeMs: 3 },
    host: { TargetFps: 18, RecentQueuedFps: 9 },
    transport: { PacingDebtMs: 2 },
  });

  assert.equal(result.code, "capture-encode");
});

test("stream debug classifies jitter buffer and decoder pressure", () => {
  assert.equal(classifyStreamBottleneck({
    client: { fps: 18, bufferMs: 180, decodeMs: 3 },
    host: { TargetFps: 18, RecentQueuedFps: 18 },
    transport: { PacingDebtMs: 2 },
  }).code, "jitter-buffer");

  assert.equal(classifyStreamBottleneck({
    client: { fps: 18, bufferMs: 10, decodeMs: 45 },
    host: { TargetFps: 18, RecentQueuedFps: 18 },
    transport: { PacingDebtMs: 2 },
  }).code, "decoder");
});

test("stream debug estimates only measurable pipeline latency components", () => {
  const view = buildStreamDebugView({
    path: "WebRTC LAN",
    client: {
      fps: 18,
      bitrateMbps: 1.2,
      width: 1280,
      height: 800,
      rttMs: 20,
      jitterMs: 2,
      bufferMs: 15,
      decodeMs: 5,
      dropped: 0,
      packetsLost: 0,
      nack: 0,
      pli: 0,
    },
    host: {
      TargetFps: 18,
      RecentQueuedFps: 18,
      RecentMbps: 1.2,
      RecentSkippedFps: 0,
      Width: 1280,
      Height: 800,
      CapturePath: "d3d11-gpu",
      Encoder: "h264_nvenc",
      QualityTier: "full",
    },
    session: {
      Transport: {
        PacingDebtMs: 4,
        TargetBitrateKbps: 1200,
        NackRequests: 0,
        PacketsRetransmitted: 0,
        PliRequests: 0,
      },
    },
  });

  assert.equal(view.estimatedLatencyMs, 34);
  assert.equal(view.bottleneck.code, "healthy");
  assert.match(view.summary, /34ms/);
  assert.match(view.client, /18fps/);
  assert.match(view.host, /h264_nvenc/);
  assert.match(view.transport, /PACER 4ms/);
});
