import test from "node:test";
import assert from "node:assert/strict";
import { shouldPreferWebRtcDisplay } from "../../src/VibeDeck.Host/wwwroot/modules/env-detect.js";

test("secure desktop browser prefers WebRTC when RTCPeerConnection is available", () => {
  assert.equal(shouldPreferWebRtcDisplay({
    hasPeerConnection: true,
    secureContext: true,
  }), true);
});

test("loopback browser prefers WebRTC without HTTPS", () => {
  assert.equal(shouldPreferWebRtcDisplay({
    hasPeerConnection: true,
    loopback: true,
  }), true);
});

test("insecure remote browser stays on JPEG in auto mode", () => {
  assert.equal(shouldPreferWebRtcDisplay({
    hasPeerConnection: true,
  }), false);
});

test("browser without RTCPeerConnection stays on JPEG in auto mode", () => {
  assert.equal(shouldPreferWebRtcDisplay({
    secureContext: true,
  }), false);
});

test("explicit webrtc query override still attempts WebRTC", () => {
  assert.equal(shouldPreferWebRtcDisplay({ forced: true }), true);
});
