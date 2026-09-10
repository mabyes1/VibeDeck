import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("H.264 failures retain actionable server diagnostics", async () => {
  const [service, streamer] = await Promise.all([
    readFile(new URL("../../src/VibeDeck.Host/Streaming/WebRtcH264Service.cs", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/Streaming/H264AnnexBStreamer.cs", import.meta.url), "utf8"),
  ]);

  assert.match(service, /audit\.RecordException\([\s\S]*"h264-session"/);
  for (const field of ["stage", "session", "encoder", "connection", "ice", "rootErrorType", "rootErrorMessage", "exception"]) {
    assert.match(service, new RegExp(`\\["${field}"\\]`));
  }
  assert.match(streamer, /ReadErrorTailAsync/);
  assert.match(streamer, /ffmpeg encoder '[^']*\{encoderName\}[^']*' ended unexpectedly/);
});

test("dead WebRTC peers use bounded full-session rebuild before JPEG", async () => {
  const controller = await readFile(
    new URL("../../src/VibeDeck.Host/wwwroot/modules/stream-controller.js", import.meta.url),
    "utf8");

  assert.match(controller, /WEBRTC_SESSION_REBUILD_LIMIT\s*=\s*2/);
  assert.match(controller, /async function rebuildRtcSession/);
  assert.match(controller, /await connect\(\{ preserveRecoveryBudget: true \}\)/);
  assert.match(controller, /rtcSessionRebuildAttempts >= WEBRTC_SESSION_REBUILD_LIMIT/);
  assert.match(controller, /fallbackToJpeg\(generation/);
  assert.doesNotMatch(controller, /restartIce\(\)/);
});

test("initial WebRTC negotiation cannot remain stuck forever", async () => {
  const controller = await readFile(
    new URL("../../src/VibeDeck.Host/wwwroot/modules/stream-controller.js", import.meta.url),
    "utf8");

  assert.match(controller, /WEBRTC_INITIAL_CONNECT_TIMEOUT_MS\s*=\s*18000/);
  assert.match(controller, /scheduleInitialConnectRecovery\(peer, generation\)/);
  assert.match(controller, /initial-connect-timeout/);
  assert.match(controller, /rebuildRtcSession\(peer, generation, "initial-connect-timeout"\)/);
  assert.match(controller, /後自動重試/);
});
