import assert from "node:assert/strict";
import test from "node:test";

import {
  buildDiagnosticsText,
  formatAuditTime,
  readAuditField,
} from "../../src/VibeDeck.Host/wwwroot/modules/diagnostics-controller.js";

const tLegacy = value => value;

test("audit field reader accepts camelCase before PascalCase", () => {
  assert.equal(readAuditField({ TraceId: "pascal", traceId: "camel" }, "TraceId", "traceId"), "camel");
  assert.equal(readAuditField({ TraceId: "pascal" }, "TraceId", "traceId"), "pascal");
});

test("audit time keeps invalid source text and formats valid time", () => {
  assert.equal(formatAuditTime("not-a-date", "en-US"), "not-a-date");
  assert.notEqual(formatAuditTime("2026-08-09T00:00:00Z", "en-US"), "--");
});

test("diagnostics text preserves trace, subject and details", () => {
  const text = buildDiagnosticsText({
    generatedAt: "2026-08-09T00:01:00Z",
    retentionDays: 7,
    entries: [{
      timestamp: "2026-08-09T00:00:00Z",
      severity: "warning",
      category: "device",
      action: "pair",
      outcome: "denied",
      traceId: "trace-1",
      subject: "Phone",
      details: { reason: "user" },
    }],
  }, {
    locale: "en-US",
    tLegacy,
  });

  assert.match(text, /device\/pair/);
  assert.match(text, /trace-1/);
  assert.match(text, /Phone/);
  assert.match(text, /reason: user/);
  assert.match(text, /保留 7 天/);
});
