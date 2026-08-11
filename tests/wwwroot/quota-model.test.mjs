import assert from "node:assert/strict";
import test from "node:test";

import {
  buildQuotaViewState,
  buildQuotaTabs,
  getProviderFamily,
  groupAgyAccounts,
  groupSingleProviderAccounts,
  providerContains,
  quotaDataFingerprint,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-model.js";

test("provider family accepts explicit family and legacy id inference", () => {
  assert.equal(getProviderFamily({ Family: "codex" }), "codex");
  assert.equal(getProviderFamily({ id: "agy-claude-account" }), "agy");
  assert.equal(getProviderFamily({ Id: "claude-local" }), "claude-code");
  assert.equal(getProviderFamily({ id: "custom-provider" }), "custom-provider");
});

test("quota tabs keep AGY and Codex visible and append discovered families", () => {
  assert.deepEqual(buildQuotaTabs([{ Family: "claude-code" }, { Family: "other" }]), [
    { id: "agy", label: "AGY" },
    { id: "codex", label: "Codex" },
    { id: "claude-code", label: "Claude" },
    { id: "other", label: "other" },
  ]);
});

test("AGY providers group by account identity and preserve provider rows", () => {
  const accounts = groupAgyAccounts([
    { Family: "agy", AccountId: "b", AccountEmail: "b@example.com", AccountTier: "PRO", Label: "AGY Claude" },
    { family: "agy", accountId: "a", accountEmail: "a@example.com", accountTier: "FREE", label: "AGY Claude" },
    { Family: "agy", AccountId: "a", AccountEmail: "a@example.com", AccountTier: "FREE", Label: "AGY Gemini" },
    { Family: "codex", AccountId: "ignored" },
  ]);

  assert.deepEqual(accounts.map(account => account.id), ["a", "b"]);
  assert.equal(accounts[0].providers.length, 2);
  assert.equal(accounts[0].email, "a@example.com");
  assert.equal(accounts[0].tier, "FREE");
});

test("single-provider accounts sort most recently observed first", () => {
  const accounts = groupSingleProviderAccounts([
    { Family: "codex", AccountId: "older", ObservedAt: "2026-08-01T00:00:00Z" },
    { Family: "agy", AccountId: "ignored", ObservedAt: "2026-08-09T00:00:00Z" },
    { Family: "codex", AccountId: "newer", ObservedAt: "2026-08-08T00:00:00Z" },
  ], "codex");

  assert.deepEqual(accounts.map(account => account.AccountId), ["newer", "older"]);
});

test("fingerprint is stable across provider order and tracks quota data", () => {
  const first = {
    Providers: [
      { Family: "codex", AccountId: "b", Id: "codex-b", State: "ok", Primary: { RemainingPercent: 70 } },
      { Family: "agy", AccountId: "a", Id: "agy-a", State: "ok", Primary: { RemainingPercent: 20 } },
    ]
  };
  const reordered = { Providers: [...first.Providers].reverse() };
  const changed = {
    Providers: [
      first.Providers[0],
      { ...first.Providers[1], Primary: { RemainingPercent: 19 } },
    ]
  };

  assert.equal(quotaDataFingerprint(first), quotaDataFingerprint(reordered));
  assert.notEqual(quotaDataFingerprint(first), quotaDataFingerprint(changed));
});

test("providerContains searches id and label case-insensitively", () => {
  assert.equal(providerContains({ Id: "agy-claude", Label: "AGY Claude" }, "CLAUDE"), true);
  assert.equal(providerContains({ id: "agy-gemini", label: "AGY Gemini" }, "claude"), false);
});

test("quota view state normalizes active tab and precomputes family account groups", () => {
  const state = buildQuotaViewState({
    Providers: [
      { Family: "agy", AccountId: "agy-1", AccountEmail: "agy@example.com", State: "source-needed" },
      { Family: "codex", AccountId: "codex-old", AccountEmail: "old@example.com", State: "source-needed", ObservedAt: "2026-08-01T00:00:00Z" },
      { Family: "codex", AccountId: "codex-live", AccountEmail: "live@example.com", State: "ok", ObservedAt: "2026-08-09T00:00:00Z" },
    ]
  }, "missing-tab");

  assert.equal(state.activeTab, "agy");
  assert.equal(state.activeDefinition.label, "AGY");
  assert.equal(state.hasUsable, true);
  assert.equal(state.agyAccounts.length, 1);
  assert.deepEqual(state.codexProviders.map(item => item.AccountId), ["codex-live", "codex-old"]);
  assert.equal(state.codexUsable, true);
  assert.deepEqual(state.tabProviders, []);

  const codex = buildQuotaViewState({ Providers: state.providers }, "codex");
  assert.equal(codex.activeTab, "codex");
  assert.equal(codex.hasUsable, true);
  assert.deepEqual(codex.tabProviders.map(item => item.AccountId), ["codex-live", "codex-old"]);
});
