import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const indexPath = new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url);

async function indexSource() {
  return readFile(indexPath, "utf8");
}

test("index composition root does not reclaim extracted lifecycle state", async () => {
  const source = await indexSource();
  const forbidden = [
    /\bwakeLock\b/,
    /\bkeepAwakeDesired\b/,
    /\bkeepAwakeWatchTimer\b/,
    /\bproductUpdateSnapshot\b/,
    /\bproductUpdatePollTimer\b/,
    /\bdisplayInstallTimer\b/,
    /\bapprovalPollTimer\b/,
    /\bresolvedPairingInfo\b/,
    /\bselectedDisplayName\b/,
    /\bselectedDisplay\b/,
    /\bavailableDisplays\b/,
    /\bquotaActionStatusByKey\b/,
    /\bdeviceManagementDefaultApplied\b/,
    /\bdiagnosticsLoading\b/,
    /\blatestAuditTrail\b/,
    /\bhostAuthEnabled\b/,
    /\bhostAuthenticated\b/,
    /\bhostAuthRequired\b/,
    /\bloadHostAuthStatus\b/,
    /\bloginToHost\b/,
    /\bupdateHostAuthGate\b/,
  ];

  for (const pattern of forbidden) {
    assert.doesNotMatch(source, pattern, `index.js reclaimed extracted state: ${pattern}`);
  }
});

test("index composition root keeps extracted subsystem owners explicit", async () => {
  const source = await indexSource();
  const owners = [
    "createQuotaActionController",
    "createQuotaCardRenderer",
    "createDiagnosticsController",
    "createDeviceManagementView",
    "createDeviceActionsController",
    "createPairingSessionController",
    "createProductUpdateController",
    "createDisplayInstallController",
    "createTurnSettingsController",
    "createKeepAwakeController",
    "createDisplaySourceController",
    "createHostAuthController",
  ];

  for (const owner of owners) {
    assert.match(source, new RegExp(`\\b${owner}\\b`), `missing composition owner: ${owner}`);
  }
});

test("index stays below the pre-refactor God-module ceiling", async () => {
  const source = await indexSource();
  const lines = source.split(/\r?\n/).length;

  // Baseline for this refactor was 5,346 lines. This is deliberately generous:
  // the test blocks a wholesale collapse back into index.js without turning
  // line count into a vanity metric for normal feature growth.
  assert.ok(lines < 4_000, `index.js grew back into a God module (${lines} lines)`);
});
