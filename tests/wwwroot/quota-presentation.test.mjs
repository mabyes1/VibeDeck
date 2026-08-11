import assert from "node:assert/strict";
import test from "node:test";

import {
  buildQuotaHelpSpec,
  buildQuotaSummary,
  buildQuotaTimestampState,
  formatCodexCreditBalance,
  formatQuotaProviderDetail,
  formatQuotaStateLabel,
} from "../../src/VibeDeck.Host/wwwroot/modules/quota-presentation.js";

const translate = value => `t:${value}`;

test("quota summary preserves provider-specific status wording", () => {
  assert.equal(buildQuotaSummary({ activeTab: "agy", agyAccounts: [{}, {}] }), "AGY · 2 個帳號");
  assert.equal(buildQuotaSummary({ activeTab: "codex", codexUsable: true, codexProviders: [{}, {}] }), "Codex · 2 個帳號 · 已讀到額度");
  assert.equal(buildQuotaSummary({
    activeTab: "claude-code",
    activeDefinition: { label: "Claude" },
    hasUsable: false,
    tabProviders: [{ State: "source-needed" }],
  }, translate), "Claude · t:等待來源");
});

test("quota help spec describes in-process Codex reauth without switching active login", () => {
  const desktop = buildQuotaHelpSpec("codex", { codexUsable: true }, false);
  assert.equal(desktop.title, "Codex 資料來源");
  assert.equal(desktop.steps.length, 4);
  assert.match(desktop.steps[2], /OAuth in-process/);
  assert.match(desktop.steps[2], /identity verification/);

  const eink = buildQuotaHelpSpec("codex", { codexUsable: false }, true);
  assert.equal(eink.title, "Codex bind");
  assert.match(eink.steps[2], /不會切換目前使用中的 Codex/);
});

test("quota state and provider detail remain translation-injected pure functions", () => {
  assert.equal(formatQuotaStateLabel("ok", translate), "t:來源可用");
  assert.equal(formatQuotaStateLabel("source-needed", translate), "t:等待來源");
  assert.equal(formatQuotaProviderDetail({ Family: "codex", State: "source-needed" }, translate), "t:尚未讀到 Codex 額度。請先使用一次 Codex，再按更新。");
  assert.equal(formatQuotaProviderDetail({ Family: "claude-code", State: "offline", Detail: "sign-in has expired" }, translate), "t:Claude Code 登入已過期。請在這台 PC 執行一次 Claude Code，再按更新。");
});

test("Codex credit formatter handles unlimited, numeric and missing balances", () => {
  assert.equal(formatCodexCreditBalance({ CreditUnlimited: true }, "en-US"), "∞");
  assert.equal(formatCodexCreditBalance({ CreditBalance: 1234.5 }, "en-US"), "1,234.5");
  assert.equal(formatCodexCreditBalance({}, "en-US"), "");
});

test("quota timestamp presentation prefers newest observation and marks stale sources", () => {
  const fresh = buildQuotaTimestampState([
    { State: "ok", ObservedAt: "2026-08-08T01:00:00Z" },
    { State: "ok", ObservedAt: "2026-08-08T02:00:00Z" },
  ], { GeneratedAt: "2026-08-08T03:00:00Z" }, { locale: "en-US", translateLegacy: translate });
  assert.equal(fresh.stale, false);
  assert.match(fresh.text, /2026/);

  const stale = buildQuotaTimestampState([
    { State: "offline", ObservedAt: "2026-08-08T02:00:00Z" },
  ], {}, { locale: "en-US", translateLegacy: translate });
  assert.equal(stale.stale, true);
  assert.match(stale.text, /t:來源離線$/);

  const unavailable = buildQuotaTimestampState([{ State: "offline" }], {}, { locale: "en-US", translateLegacy: translate });
  assert.deepEqual(unavailable, { stale: true, text: "t:來源不可用" });
});
