import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("standalone quota navigation is hidden while Sideboard owns quota UI", async () => {
  const html = await readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.html", import.meta.url), "utf8");
  assert.match(html, /id="quotaMode"[^>]*hidden/);
  assert.doesNotMatch(html, /data-dashboard-mode="quota"/);
  assert.doesNotMatch(html, /data-dashboard-mode="sideboard"/);
  assert.doesNotMatch(html, /class="dashboard-mode-switch"/);
});
