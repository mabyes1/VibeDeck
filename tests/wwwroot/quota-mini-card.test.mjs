import test from "node:test";
import assert from "node:assert/strict";

import {
  quotaMiniFamilyOf,
  resolveQuotaMiniDefaultProvider,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-mini-card.js";

test("quota mini family normalizes explicit and legacy provider ids", () => {
  assert.equal(quotaMiniFamilyOf({ Family: "codex" }), "codex");
  assert.equal(quotaMiniFamilyOf({ Id: "claude-code-account" }), "claude-code");
  assert.equal(quotaMiniFamilyOf({ Id: "agy-gemini-account" }), "agy");
});

test("quota mini defaults to active Codex instead of newest inactive account", () => {
  const providers = [
    {
      Family: "codex",
      AccountEmail: "newer@example.com",
      IsActive: false,
      ObservedAt: "2026-08-10T06:20:00Z",
    },
    {
      Family: "codex",
      AccountEmail: "active@example.com",
      IsActive: true,
      ObservedAt: "2026-08-10T05:54:00Z",
    },
    {
      Family: "agy",
      AccountEmail: "agy@example.com",
      IsActive: false,
      ObservedAt: "2026-08-10T06:30:00Z",
    },
  ];

  assert.equal(resolveQuotaMiniDefaultProvider(providers).AccountEmail, "active@example.com");
});

test("quota mini falls back to newest source when no account is active", () => {
  const providers = [
    { Family: "codex", AccountEmail: "old@example.com", ObservedAt: "2026-08-09T01:00:00Z" },
    { Family: "agy", AccountEmail: "new@example.com", ObservedAt: "2026-08-10T01:00:00Z" },
  ];

  assert.equal(resolveQuotaMiniDefaultProvider(providers).AccountEmail, "new@example.com");
});
