import assert from "node:assert/strict";
import test from "node:test";

import {
  hasPendingPairingRecord,
  resolvePairingDisplayName,
} from "../../src/VibeDeck.Host/wwwroot/modules/pairing-session-controller.js";

test("pairing display name preserves existing model naming rules", () => {
  assert.equal(resolvePairingDisplayName("Android device", "sm-s9110"), "Samsung SM-S9110");
  assert.equal(resolvePairingDisplayName("E-paper device", "GoColor7"), "BOOX Go Color 7");
  assert.equal(resolvePairingDisplayName("E-paper device", "BOOX Go Color 7"), "BOOX Go Color 7");
  assert.equal(resolvePairingDisplayName("Android device", "Pixel 9"), "Pixel 9");
  assert.equal(resolvePairingDisplayName("Android device", "K"), "Android device");
  assert.equal(resolvePairingDisplayName("Web device", ""), "Web device");
});

test("pending pairing record requires both id and secret", () => {
  assert.equal(hasPendingPairingRecord({ requestId: "id", requestSecret: "secret" }), true);
  assert.equal(hasPendingPairingRecord({ requestId: "id" }), false);
  assert.equal(hasPendingPairingRecord({ requestSecret: "secret" }), false);
  assert.equal(hasPendingPairingRecord(null), false);
});
