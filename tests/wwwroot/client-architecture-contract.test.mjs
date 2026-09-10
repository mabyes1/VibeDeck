import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";
import test from "node:test";

const webRoot = new URL("../../src/VibeDeck.Host/wwwroot/", import.meta.url);
const hostRoot = new URL("../../src/VibeDeck.Host/", import.meta.url);

async function read(path, root = webRoot) {
  return readFile(new URL(path, root), "utf8");
}

async function collectCss(directory = new URL("css/", webRoot)) {
  const entries = await readdir(directory, { withFileTypes: true });
  const chunks = await Promise.all(entries.map(async entry => {
    const url = new URL(entry.name + (entry.isDirectory() ? "/" : ""), directory);
    if (entry.isDirectory()) return collectCss(url);
    if (!entry.name.endsWith(".css")) return [];
    return [{ name: url.pathname, text: await readFile(url, "utf8") }];
  }));
  return chunks.flat();
}

test("mobile and tablet layout stay capability/space driven instead of browser archaeology", async () => {
  const [index, env, dashboardLayouts, cssFiles] = await Promise.all([
    read("index.js"),
    read("modules/env-detect.js"),
    read("Dashboard/DashboardLayoutService.cs", hostRoot),
    collectCss(),
  ]);
  const css = cssFiles.map(file => file.text).join("\n");

  assert.doesNotMatch(index + css, /phone-client|phone-force-landscape|space-sideboard-|viewport-(?:portrait|landscape)/);
  assert.doesNotMatch(css, /body\.mobile-client|\.mobile-client[\s:{]/);
  assert.doesNotMatch(css, /@media\s*\((?:max|min)-(?:width|height)/);
  assert.doesNotMatch(env, /webOS|BlackBerry|IEMobile|Opera Mini/);
  assert.match(env, /Android/);
  assert.match(env, /isIosUA/);
  assert.doesNotMatch(dashboardLayouts, /tablet-(?:portrait|landscape)/);
});

test("E-Ink remains the explicit alternate presentation", async () => {
  const [core, shell] = await Promise.all([
    read("css/10-core.css"),
    read("css/components/sideboard-shell.css"),
  ]);

  assert.match(core, /body\.eink-client/);
  assert.match(shell, /body\.eink-client/);
});
