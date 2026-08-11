import assert from "node:assert/strict";
import test from "node:test";

import {
  createQuotaAccountNavigator,
  moveQuotaAccountSelection,
  quotaAccountKey,
  resolveQuotaAccountSelection,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-account-navigation.js";

const accounts = [
  { AccountId: "a", AccountEmail: "a@example.com" },
  { AccountId: "b", AccountEmail: "b@example.com" },
  { AccountId: "c", AccountEmail: "c@example.com" },
];

test("quota account keys prefer stable account identity", () => {
  assert.equal(quotaAccountKey(accounts[0], "codex"), "a");
  assert.equal(quotaAccountKey({ id: "agy-a", email: "agy@example.com" }, "agy"), "agy-a");
});

test("selection preserves the selected account when the list is reordered", () => {
  const selected = resolveQuotaAccountSelection(accounts, "codex", 1, "b");
  assert.deepEqual(selected, { index: 1, key: "b" });

  const reordered = [accounts[1], accounts[0], accounts[2]];
  assert.deepEqual(resolveQuotaAccountSelection(reordered, "codex", selected.index, selected.key), {
    index: 0,
    key: "b",
  });
});

test("move wraps in both directions", () => {
  assert.deepEqual(moveQuotaAccountSelection(accounts, "codex", 0, "a", -1), { index: 2, key: "c" });
  assert.deepEqual(moveQuotaAccountSelection(accounts, "codex", 2, "c", 1), { index: 0, key: "a" });
});

test("navigator owns per-tab selection state without leaking globals into the page", () => {
  const navigator = createQuotaAccountNavigator();
  assert.equal(navigator.getIndex("codex", accounts), 0);
  assert.equal(navigator.move("codex", accounts, 1), 1);
  assert.equal(navigator.getIndex("codex", accounts), 1);

  const reordered = [accounts[1], accounts[0], accounts[2]];
  assert.equal(navigator.getIndex("codex", reordered), 0);
  assert.equal(navigator.getIndex("agy", [{ id: "agy-a" }, { id: "agy-b" }]), 0);
});
