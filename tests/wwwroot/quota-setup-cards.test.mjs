import assert from "node:assert/strict";
import test from "node:test";

import { buildQuotaSetupSpec } from "../../src/VibeDeck.Host/wwwroot/modules/quota-setup-cards.js";

const t = key => `t:${key}`;

test("AGY setup spec preserves OAuth actions and desktop endpoint hint", () => {
  const spec = buildQuotaSetupSpec("agy", false, t);

  assert.equal(spec.statusKey, "agy:setup");
  assert.equal(spec.footer, "POST /api/quotas/agy/oauth/start");
  assert.deepEqual(spec.actions.map(action => action.action), ["agy-oauth", "refresh"]);
  assert.match(spec.actions[0].text, /t:ui\.agyAddQuotaAccount/);
});

test("Codex setup spec exposes quota reauthorization without switching active login", () => {
  const eink = buildQuotaSetupSpec("codex", true, t);
  const desktop = buildQuotaSetupSpec("codex", false, t);

  assert.equal(eink.title, "Codex 資料來源");
  assert.equal(eink.footer, "OAuth + account usage");
  assert.match(desktop.lines[1], /重新授權額度/);
  assert.match(desktop.lines[2], /不會切換目前使用中的 Codex/);
  assert.equal(desktop.actions.length, 1);
});

test("Claude setup spec preserves read-only token guidance", () => {
  const spec = buildQuotaSetupSpec("claude-code", false, t);

  assert.equal(spec.statusKey, "claude-code:setup");
  assert.match(spec.lines[1], /只讀不續期/);
  assert.match(spec.lines[2], /不會保存 token/);
});

test("unknown setup family fails closed", () => {
  assert.throws(() => buildQuotaSetupSpec("mystery", false, t), /Unsupported quota setup family/);
});
