import test from "node:test";
import assert from "node:assert/strict";
import { createQuotaController } from "../../src/VibeDeck.Host/wwwroot/modules/quota-controller.js";

function createHarness(activeMode = "sideboard") {
  const requests = [];
  const rendered = [];
  const controller = createQuotaController({
    elements: {
      quotaGrid: { childElementCount: 1 },
      quotaSummary: { textContent: "" },
      quotaUpdated: { textContent: "" },
      quotaHelp: null,
    },
    getActiveMode: () => activeMode,
    fetchJsonOrThrow: async (url, init) => {
      requests.push({ url, init });
      return { Providers: [] };
    },
    isTrustRequiredError: () => false,
    renderSnapshot: snapshot => rendered.push(snapshot),
    renderErrorHelp: () => null,
    onConnectionChange: () => {},
  });
  return { controller, requests, rendered };
}

test("quota refresh stays dormant off-page unless explicitly backgrounded", async () => {
  const harness = createHarness("sideboard");

  const result = await harness.controller.refresh({ force: true });

  assert.equal(result, null);
  assert.equal(harness.requests.length, 0);
});

test("global recovery can force-refresh quota while Sideboard is active", async () => {
  const harness = createHarness("sideboard");

  await harness.controller.refresh({ force: true, background: true });

  assert.equal(harness.requests.length, 1);
  assert.equal(harness.requests[0].url, "/api/quotas/refresh");
  assert.equal(harness.requests[0].init.method, "POST");
  assert.equal(harness.rendered.length, 1);
});
