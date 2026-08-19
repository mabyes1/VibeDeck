import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

test("standalone quota navigation is hidden while Sideboard owns quota UI", async () => {
  const html = await readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.html", import.meta.url), "utf8");
  assert.match(html, /id="quotaMode"[^>]*hidden/);
  const quotaSwitches = [...html.matchAll(/<button[^>]*data-dashboard-mode="quota"[^>]*>/g)];
  assert.equal(quotaSwitches.length, 2);
  assert.ok(quotaSwitches.every(match => /\shidden(?:\s|>)/.test(match[0])));
});
