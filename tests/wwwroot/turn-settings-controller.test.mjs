import assert from "node:assert/strict";
import test from "node:test";

import {
  buildTurnDiagnosticsText,
  buildTurnSettingsView,
} from "../../src/VibeDeck.Host/wwwroot/modules/turn-settings-controller.js";

test("TURN settings view accepts both API casing styles", () => {
  assert.deepEqual(buildTurnSettingsView({ Configured: true, KeyId: "key-1", UpdatedAt: "2026-08-09" }), {
    configured: true,
    keyId: "key-1",
    updatedAt: "2026-08-09",
  });
  assert.equal(buildTurnSettingsView({ configured: false }).configured, false);
});

test("TURN diagnostics summarizes first active WebRTC session", () => {
  const text = buildTurnDiagnosticsText({
    webRtc: {
      activeSessions: [{ connectionState: "connected", iceState: "completed", transportPlan: "turn" }],
    },
  });

  assert.equal(text, "Host WebRTC：connected · ICE completed · turn");
});

test("TURN diagnostics keeps explicit empty-state guidance", () => {
  assert.match(buildTurnDiagnosticsText({}), /目前沒有活動中的 Host WebRTC 連線/);
});
