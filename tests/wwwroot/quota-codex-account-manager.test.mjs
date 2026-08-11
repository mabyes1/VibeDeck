import assert from "node:assert/strict";
import test from "node:test";

import {
  buildCodexAccountActionState,
  buildCodexProfileOption,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-codex-account-manager.js";

const t = key => ({
  "ui.unknown": "Unknown",
  "ui.codexActive": "Active",
}[key] || key);

test("Codex profile option accepts PascalCase and marks active account", () => {
  assert.deepEqual(buildCodexProfileOption({
    AccountId: "acct-1",
    Email: "ken@example.com",
    Tier: "Plus",
    IsActive: true,
  }, t), {
    accountId: "acct-1",
    email: "ken@example.com",
    tier: "Plus",
    active: true,
    value: "acct-1",
    label: "ken@example.com · Plus · Active",
  });
});

test("Codex profile option falls back through camelCase identity fields", () => {
  const option = buildCodexProfileOption({ accountId: "acct-2", isActive: false }, t);
  assert.equal(option.value, "acct-2");
  assert.equal(option.label, "acct-2");
  assert.equal(option.active, false);
});

test("Codex account actions disable switch and reauth for active account only", () => {
  assert.deepEqual(buildCodexAccountActionState({ accountId: "acct", active: true }), {
    switchDisabled: true,
    reauthDisabled: true,
    deleteDisabled: false,
  });
  assert.deepEqual(buildCodexAccountActionState({ email: "saved@example.com", active: false }), {
    switchDisabled: false,
    reauthDisabled: false,
    deleteDisabled: false,
  });
  assert.deepEqual(buildCodexAccountActionState({}), {
    switchDisabled: true,
    reauthDisabled: true,
    deleteDisabled: true,
  });
});
