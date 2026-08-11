import test from "node:test";
import assert from "node:assert/strict";
import {
  moveQuotaAccountSelection,
  resolveQuotaAccountSelection,
  sortQuotaAccountsByRecentUse,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-account-navigation.js";

const account = (id, email, observedAt, isActive = false) => ({
  AccountId: id,
  AccountEmail: email,
  ObservedAt: observedAt,
  IsActive: isActive,
});

test("quota accounts sort by recent observed use, not active login", () => {
  const olderActive = account("a", "a@example.com", "2026-08-08T08:00:00Z", true);
  const newerInactive = account("b", "b@example.com", "2026-08-08T10:00:00Z", false);

  const sorted = sortQuotaAccountsByRecentUse([olderActive, newerInactive]);

  assert.equal(sorted[0].AccountId, "b");
  assert.equal(sorted[1].AccountId, "a");
});

test("selected quota account survives a reorder after refresh", () => {
  const before = sortQuotaAccountsByRecentUse([
    account("a", "a@example.com", "2026-08-08T08:00:00Z"),
    account("b", "b@example.com", "2026-08-08T10:00:00Z"),
  ]);
  const selected = moveQuotaAccountSelection(before, "codex", 0, "", 1);
  assert.equal(before[selected.index].AccountId, "a");

  const after = sortQuotaAccountsByRecentUse([
    account("a", "a@example.com", "2026-08-08T11:00:00Z"),
    account("b", "b@example.com", "2026-08-08T10:00:00Z"),
  ]);
  const resolved = resolveQuotaAccountSelection(after, "codex", selected.index, selected.key);

  assert.equal(resolved.index, 0);
  assert.equal(after[resolved.index].AccountId, "a");
});

test("quota navigation wraps while keeping account identity", () => {
  const accounts = [
    account("a", "a@example.com", "2026-08-08T11:00:00Z"),
    account("b", "b@example.com", "2026-08-08T10:00:00Z"),
  ];

  const previous = moveQuotaAccountSelection(accounts, "codex", 0, "a", -1);

  assert.equal(previous.index, 1);
  assert.equal(previous.key, "b");
});
