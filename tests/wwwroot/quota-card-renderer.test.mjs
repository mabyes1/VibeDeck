import assert from "node:assert/strict";
import test from "node:test";

import { buildQuotaSwitcherHtml } from "../../src/VibeDeck.Host/wwwroot/modules/quota-card-renderer.js";

test("quota switcher stays empty for a single account", () => {
  assert.equal(buildQuotaSwitcherHtml(0, 1, "codex"), '<span class="quota-account-switcher"></span>');
});

test("quota switcher renders position, tab identity and active dot", () => {
  const html = buildQuotaSwitcherHtml(1, 3, "codex");

  assert.match(html, /data-tab-id="codex"/);
  assert.match(html, />2 \/ 3</);
  assert.equal((html.match(/quota-dot/g) || []).length, 4); // container + 3 dots
  assert.equal((html.match(/quota-dot active/g) || []).length, 1);
});
