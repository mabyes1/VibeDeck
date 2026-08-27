import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const root = new URL("../../src/VibeDeck.Host/wwwroot/", import.meta.url);

test("all localized manifests request fullscreen without relying on display_override", async () => {
  const manifests = await Promise.all([
    "manifest.json",
    "manifest.en.json",
    "manifest.ja.json",
  ].map(async path => JSON.parse(await readFile(new URL(path, root), "utf8"))));

  for (const manifest of manifests) {
    assert.equal(manifest.display, "fullscreen");
    assert.equal(manifest.display_override[0], "fullscreen");
    assert.match(manifest.start_url, /[?&]v=54(?:&|$)/);
    for (const shortcut of manifest.shortcuts || []) {
      assert.match(shortcut.url, /[?&]v=54(?:&|$)/);
    }
  }
});

test("locale switching does not roll the manifest cache key back to v1", async () => {
  const [html, i18n] = await Promise.all([
    readFile(new URL("index.html", root), "utf8"),
    readFile(new URL("modules/i18n.js", root), "utf8"),
  ]);

  assert.match(html, /id="manifestLink"[^>]+manifest\.json\?v=54/);
  assert.match(i18n, /link\.href\s*=\s*`\$\{target\}\?v=54`/);
  assert.doesNotMatch(i18n, /\$\{target\}\?v=1`/);
});

test("landscape tablets may use the real Fullscreen API", async () => {
  const index = await readFile(new URL("index.js", root), "utf8");

  assert.match(index, /displayUsesCssRotation\s*=\s*isDisplayMode\s*&&\s*document\.body\.classList\.contains\("force-landscape"\)/);
  assert.match(index, /useBrowserFullscreen\s*=\s*!isIos\(\)\s*&&\s*!displayUsesCssRotation/);
});

test("fullscreen PWAs enter the VibeDeck viewer so exit chrome remains meaningful", async () => {
  const index = await readFile(new URL("index.js", root), "utf8");

  assert.match(index, /matchMedia\("\(display-mode: fullscreen\)"\)\.matches/);
  assert.match(index, /function shouldAutoEnterInstalledViewer\(\)/);
  assert.match(index, /if \(isDevicePreview\(\) \|\| !deviceTrusted \|\| !isStandaloneApp\(\)\) return false/);
  assert.match(index, /initialMode !== "setup" && initialMode !== "deck" && !initialMode\.startsWith\("deck:"\)/);
  assert.match(index, /shouldStartInViewer\(\) \|\| shouldAutoEnterInstalledViewer\(\)/);
});

test("trusted Android clients can invoke the deferred native PWA install prompt", async () => {
  const [html, index] = await Promise.all([
    readFile(new URL("index.html", root), "utf8"),
    readFile(new URL("index.js", root), "utf8"),
  ]);

  assert.match(html, /id="installVibeDeck"[^>]+hidden[^>]+secureEndpoint\.installApp/);
  assert.match(index, /window\.addEventListener\("beforeinstallprompt"/);
  assert.match(index, /installPromptEvent\s*=\s*event/);
  assert.match(index, /deviceTrusted\s*&&\s*!deviceLocalRequest/);
  assert.match(index, /await promptEvent\.prompt\(\)/);
  assert.match(index, /await promptEvent\.userChoice/);
  assert.match(index, /installVibeDeck\?\.addEventListener\("click"/);
});
