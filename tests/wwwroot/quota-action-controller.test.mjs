import assert from "node:assert/strict";
import test from "node:test";

import {
  getQuotaActionMessage,
  normalizeQuotaAccountTarget,
  quotaCardMatchesTarget,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-action-controller.js";

test("quota account target strips unrelated UI state before POST", () => {
  assert.deepEqual(normalizeQuotaAccountTarget({
    accountId: "acct-1",
    email: "ken@example.com",
    label: "Ken · Active",
    active: true,
  }), {
    accountId: "acct-1",
    email: "ken@example.com",
  });
});

test("quota action message accepts both API casing styles", () => {
  assert.equal(getQuotaActionMessage({ Message: "Pascal" }, "fallback"), "Pascal");
  assert.equal(getQuotaActionMessage({ message: "camel" }, "fallback"), "camel");
  assert.equal(getQuotaActionMessage({}, "fallback"), "fallback");
});

test("Codex card matching prefers email and falls back to account id", () => {
  const card = { dataset: { accountId: "ACCT-1", accountEmail: "KEN@EXAMPLE.COM" } };

  assert.equal(quotaCardMatchesTarget(card, { email: "ken@example.com", accountId: "wrong" }), true);
  assert.equal(quotaCardMatchesTarget(card, { email: "other@example.com", accountId: "acct-1" }), false);
  assert.equal(quotaCardMatchesTarget(
    { dataset: { accountId: "ACCT-1", accountEmail: "" } },
    { accountId: "acct-1" }
  ), true);
});
