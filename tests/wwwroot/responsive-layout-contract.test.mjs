import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const cssRoot = new URL("../../src/VibeDeck.Host/wwwroot/css/", import.meta.url);

async function css(path) {
  return readFile(new URL(path, cssRoot), "utf8");
}

test("compact Sideboard is selected by container space instead of a device selector", async () => {
  const [phone, shell] = await Promise.all([
    css("30-phone-dashboard.css"),
    css("components/sideboard-shell.css"),
  ]);

  assert.match(shell, /container:\s*sideboard-view\s*\/\s*inline-size/);
  assert.match(shell, /\.sideboard-shell\s*\{[\s\S]*min-width:\s*0/);
  assert.match(phone, /@container\s+sideboard-view\s*\(max-width:\s*44rem\)/);
  assert.match(phone, /\.mobile-overview\s*\{[\s\S]*display:\s*flex[\s\S]*flex-direction:\s*column/);
  assert.doesNotMatch(phone, /\.mobile-overview\s*\{[^}]*min-height:\s*100cqh/);
  assert.doesNotMatch(phone, /\.mobile-overview\s*\{[^}]*max-height:\s*100cqh/);
  assert.match(phone, /\.mobile-quota-strip\s*\{\s*margin-top:\s*clamp\(0px,\s*2cqh,\s*16px\)/);
  assert.doesNotMatch(phone, /\.mobile-quota-strip\s*\{\s*margin-top:\s*auto/);
  assert.doesNotMatch(phone, /grid-template-rows:\s*auto\s+auto\s+auto\s+minmax\(108px,\s*1fr\)\s+auto/);
  assert.match(phone, /@container\s+sideboard-view\s*\(max-width:\s*18rem\)[\s\S]*\.mobile-overview-actions[\s\S]*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
  assert.match(shell, /@container sideboard-view[^}]*[\s\S]*\.sideboard-shell\s*\{[^}]*scrollbar-gutter:\s*auto/);
  assert.match(phone, /\.mobile-overview-section,[\s\S]*var\(--theme-glass/);
  assert.doesNotMatch(phone, /background:\s*rgba\(11,\s*27,\s*32,\s*\.88\)/);
  assert.doesNotMatch(phone, /linear-gradient\(145deg,\s*rgba\(14,\s*34,\s*40/);
  assert.doesNotMatch(phone + shell, /space-sideboard-(?:micro|compact|mid|wide)/);
  assert.doesNotMatch(phone, /@media\s*\(max-width:\s*1100px\)/);
  assert.doesNotMatch(phone, /(?:phone|mobile|tablet)-client|viewport-(?:portrait|landscape)/);
});

test("dashboard editor is one bounded scrolling workspace", async () => {
  const editor = await css("components/dashboard-editor.css");

  assert.match(editor, /body\.dashboard-edit-mode\s*\{[\s\S]*overflow-y:\s*auto/);
  assert.match(editor, /height:\s*max\(12rem,\s*calc\(var\(--viewer-height,\s*100vh\)\s*-\s*var\(--dashboard-editor-chrome/);
  assert.match(editor, /grid-template-rows:\s*repeat\(var\(--dashboard-row-count,\s*6\),\s*minmax\(0,\s*1fr\)\)/);
  assert.doesNotMatch(editor, /minmax\(min-content,\s*1fr\)/);
  assert.doesNotMatch(editor, /(?:min-)?height:\s*(?:820|720|520)px/);
  assert.doesNotMatch(editor, /dashboard-edit-mode[^,{]*(?:tablet-client|eink-client|viewport-(?:portrait|landscape))/);
  assert.doesNotMatch(editor, /!important/);
});

test("dashboard row geometry comes from the persisted layout model", async () => {
  const [shell, editor, controller] = await Promise.all([
    css("components/sideboard-shell.css"),
    css("components/dashboard-editor.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/dashboard-layout.js", import.meta.url), "utf8"),
  ]);

  assert.match(shell, /repeat\(var\(--dashboard-row-count,\s*6\)/);
  assert.match(shell, /\.sideboard-shell\s*\{[\s\S]*display:\s*grid/);
  assert.match(shell, /grid-template-rows:\s*repeat\(var\(--dashboard-row-count,\s*6\),\s*minmax\(0,\s*1fr\)\)/);
  assert.doesNotMatch(shell, /grid-template-rows:\s*repeat\(var\(--dashboard-row-count,\s*6\),\s*minmax\(90px/);
  assert.doesNotMatch(shell, /#systemSideboardPage\.dashboard-grid\s*\{[^}]*min-height:\s*520px/);
  assert.match(editor, /repeat\(var\(--dashboard-row-count,\s*6\)/);
  assert.match(controller, /grid\.style\.setProperty\("--dashboard-row-count",\s*String\(maxRows\(\)\)\)/);
  assert.match(controller, /node\.hidden\s*=\s*!visible/);
  assert.doesNotMatch(controller, /dashboard-card-hidden/);
  assert.doesNotMatch(shell, /viewport-portrait\s+#systemSideboardPage\.dashboard-grid/);
  assert.doesNotMatch(controller, /isTabletClient|tablet-(?:portrait|landscape)/);
});

test("short LCD dashboards use a two-axis CSS container without changing saved geometry", async () => {
  const [shell, metrics, activity, cards, quota, controller] = await Promise.all([
    css("components/sideboard-shell.css"),
    css("components/metric-card.css"),
    css("components/activity-feed.css"),
    css("components/custom-cards.css"),
    css("components/quota-page.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/dashboard-layout.js", import.meta.url), "utf8"),
  ]);

  assert.doesNotMatch(shell, /container:\s*dashboard-read-grid\s*\/\s*size/);
  assert.match(shell, /\.sideboard-view\.active\s*\{[\s\S]*container:\s*sideboard-view\s*\/\s*size/);
  for (const owner of [shell, metrics, activity, cards, quota]) {
    assert.match(owner, /@container\s+sideboard-view\s*\(max-height:\s*30rem\)/);
    assert.doesNotMatch(owner, /dashboard-short-canvas/);
  }
  assert.match(shell, /min\(3\.4cqw,\s*5\.8cqh\)/);
  assert.match(metrics, /min\(3\.7cqw,\s*6\.2cqh\)/);
  assert.match(cards, /min\(4cqw,\s*7cqh\)/);
  assert.match(metrics, /--metric-value-size:\s*18px/);
  assert.match(activity, /\.activity-feed-text\s*\{[\s\S]*font-size:\s*10px/);
  assert.match(cards, /\.custom-status-value,[\s\S]*font-size:\s*18px/);
  assert.match(quota, /--quota-window-value-size:\s*12px/);
  assert.doesNotMatch(controller, /syncReadDensity|dashboard-short-canvas|height\s*<=\s*416/);
});

test("Device Lab keeps Android and iPhone viewer fullscreen on the same CSS path", async () => {
  const index = await readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8");
  assert.match(index, /const useBrowserFullscreen = !isDevicePreview\(\) && !isIos\(\) && !displayUsesCssRotation/);
});

test("dashboard fullscreen reuses existing controls instead of reserving or overlaying viewer space", async () => {
  const [html, core, navigation, mobile, index] = await Promise.all([
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.html", import.meta.url), "utf8"),
    css("10-core.css"),
    css("components/dashboard-navigation.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/mobile-overview.js", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8"),
  ]);
  assert.match(html, /data-eink-connection-state[^>]*>CONNECTING<\/span>\s*<button id="dashboardExitViewer"/);
  assert.match(navigation, /body\.dashboard-viewer\.remote-client:not\(\.eink-client\) \.dashboard-exit-viewer/);
  assert.doesNotMatch(core, /dashboard-viewer\.mode-sideboard[^}]*padding-top:\s*42px/);
  assert.doesNotMatch(core, /dashboard-viewer\.mode-sideboard[^}]*\.exit-viewer[^}]*left:\s*50%/);
  assert.match(mobile, /classList\.contains\("dashboard-viewer"\)[\s\S]*byId\("exitViewer"\)\?\.click/);
  assert.match(index, /dashboardExitViewer\?\.addEventListener\("click",\s*exitLandscapeViewer\)/);
});

test("iPhone landscape Sideboard content uses only the physical notch side", async () => {
  const [index, shell] = await Promise.all([
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8"),
    css("components/sideboard-shell.css"),
  ]);
  assert.match(index, /function syncSideboardContentSafeArea\(width, height\)/);
  assert.match(index, /angle === 90[\s\S]*--sideboard-content-inset-left[\s\S]*--sideboard-content-inset-right", "0px"/);
  assert.match(index, /angle === 270[\s\S]*--sideboard-content-inset-left", "0px"[\s\S]*--sideboard-content-inset-right/);
  assert.match(shell, /--sideboard-content-inset-right/);
  assert.match(shell, /--sideboard-content-inset-left/);
});

test("remote dashboard waits for trusted layout data and never retries an invalid empty board", async () => {
  const [controller, index] = await Promise.all([
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/dashboard-layout.js", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8"),
  ]);

  assert.match(controller, /if \(!layoutLoaded\) return false/);
  assert.match(controller, /if \(!layoutLoaded \|\| !items\.some\(item => item\.visible\)\)/);
  assert.match(controller, /if \(saveSucceeded && \(autoSaveQueued \|\| layoutSignature\(\) !== lastSavedSignature\)\)/);
  assert.doesNotMatch(controller, /\n\s*load\(\);\s*\n\s*return \{/);
  assert.match(index, /await dashboardLayoutController\?\.loadIfNeeded\?\.\(\)/);
});

test("ordinary PC content scrolls instead of being clipped", async () => {
  const [core, shell, tokens] = await Promise.all([
    css("10-core.css"),
    css("components/sideboard-shell.css"),
    css("00-base-tokens.css"),
  ]);
  assert.match(core, /body\.pc-console:not\(\.mode-setup\) main\s*\{[\s\S]*overflow-y:\s*auto/);
  assert.match(core, /body\.pc-console\s*\{[^}]*height:\s*var\(--viewer-height,\s*100vh\)/);
  assert.match(shell, /body\.mode-sideboard:not\(\.dashboard-edit-mode\):not\(\.eink-client\)\s*\{[^}]*height:\s*var\(--viewer-height,\s*100vh\)/);
  assert.match(tokens, /--viewer-height:\s*100vh/);
});

test("application shell geometry is remote-capability and container driven", async () => {
  const [core, index] = await Promise.all([
    css("10-core.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8"),
  ]);
  assert.match(index, /classList\.toggle\("remote-client",\s*!localConsole\)/);
  assert.match(index, /"linux-desktop"/);
  assert.match(index, /MOBILE_DEVICE_PREVIEW_KINDS\.has\(devicePreviewKind\)/);
  assert.match(core, /@container\s+app-header\s*\(max-width:\s*48rem\)/);
  assert.doesNotMatch(core, /body\.(?:phone|mobile|tablet)-client|viewport-(?:portrait|landscape)/);
  assert.doesNotMatch(core, /@media\s*\((?:max|min)-(?:width|height)/);
});

test("logical landscape chrome consumes one authoritative state class", async () => {
  const [core, controls, shell, quota] = await Promise.all([
    css("10-core.css"),
    css("components/app-controls.css"),
    css("components/sideboard-shell.css"),
    css("components/quota-page.css"),
  ]);
  assert.match(core, /html\.mobile-force-landscape body\.force-landscape\s*\{/);
  for (const owner of [controls, shell, quota]) {
    assert.doesNotMatch(owner, /html\.mobile-force-landscape body\.force-landscape/);
  }
});

test("E-Ink Sideboard reserves viewer chrome and keeps content reachable", async () => {
  const shell = await css("components/sideboard-shell.css");
  assert.match(shell, /viewer-immersive\)[^{]*\.sideboard-shell[\s\S]*padding:\s*12px 14px calc\(58px \+ var\(--safe-area-inset-bottom/);
  assert.match(shell, /body\.eink-client #systemSideboardPage\.dashboard-grid\s*\{[\s\S]*overflow:\s*auto/);
});

test("quota identity text can break at arbitrary narrow widths", async () => {
  const [quota, mini] = await Promise.all([
    css("components/quota-page.css"),
    css("components/quota-mini-card.css"),
  ]);
  assert.match(quota, /\.quota-account-email\s*\{[\s\S]*min-width:\s*0;[\s\S]*overflow-wrap:\s*anywhere/);
  assert.match(quota, /container:\s*quota-view\s*\/\s*inline-size/);
  assert.match(quota, /@container\s+quota-view\s*\(max-width:\s*44rem\)/);
  assert.match(quota, /body\.pc-console\.mode-quota \.quota-view\.active\s*\{[^}]*height:\s*max-content/);
  assert.doesNotMatch(quota, /tablet-client:is\([^)]*dashboard-viewer[^}]*overflow:\s*hidden/);
  assert.doesNotMatch(quota, /\.quota-more-actions[^}]*!important/);
  assert.match(quota, /\.quota-toolbox\s*>\s*button/);
  assert.match(mini, /\.quota-mini-card\s*\{[^}]*overflow:\s*auto/);
  assert.match(mini, /\.quota-mini-credit\s*\{[^}]*overflow-wrap:\s*anywhere/);
  assert.doesNotMatch(mini, /\.quota-mini-credit\s*\{[^}]*text-overflow:\s*ellipsis/);
  assert.equal((mini.match(/body\.eink-client \.quota-mini-card\s*\{/g) || []).length, 1);
});

test("LCD product surfaces share the Host-owned neutral glass material", async () => {
  const [tokens, core, shell, activity, mini, quota, setup, pairing] = await Promise.all([
    css("00-base-tokens.css"),
    css("10-core.css"),
    css("components/sideboard-shell.css"),
    css("components/activity-feed.css"),
    css("components/quota-mini-card.css"),
    css("components/quota-page.css"),
    css("components/setup-diagnostics.css"),
    css("components/pairing-setup.css"),
  ]);

  assert.match(tokens, /--theme-surface:[\s\S]*var\(--theme-glass\)/);
  assert.match(tokens, /--theme-surface-strong:[\s\S]*var\(--theme-glass-strong\)/);
  assert.match(quota, /\.quota-shell\s*\{[\s\S]*background:\s*transparent/);
  assert.match(quota, /\.quota-account-card\s*\{[\s\S]*background:\s*var\(--theme-surface\)/);
  assert.doesNotMatch(quota, /\.quota-codex-switcher\s*\{[^}]*background:/);
  assert.doesNotMatch(quota, /\.quota-codex-switcher\s*\{[^}]*border:/);
  assert.doesNotMatch(quota, /radial-gradient\(circle at 62% 52%,\s*rgba\(77,\s*207,\s*255/);
  assert.match(setup, /\.setup-panels \.panel\s*\{[\s\S]*background:\s*var\(--theme-surface\)/);
  assert.match(pairing, /\.connect-panel\s*\{[\s\S]*background:\s*var\(--theme-surface\)/);
  assert.match(core, /body\.viewer-immersive:not\(\.dashboard-viewer\) \.exit-viewer/);
  assert.match(core, /\.exit-viewer\s*\{[\s\S]*z-index:\s*5000/);
  assert.match(shell, /body\.mode-sideboard:not\(\.dashboard-edit-mode\):not\(\.eink-client\) \.side-panel/);
  assert.match(activity, /body\.mode-sideboard:not\(\.eink-client\) \.activity-feed-item\s*\{[\s\S]*linear-gradient/);
  assert.match(mini, /body\.mode-sideboard:not\(\.eink-client\) \.quota-mini-bar span\s*\{[\s\S]*var\(--grad-accent\)/);
  assert.doesNotMatch(activity, /body\.pc-console\.mode-sideboard \.activity-feed-item\s*\{/);
  assert.doesNotMatch(mini, /body\.pc-console\.mode-sideboard \.quota-mini-bar/);
});

test("Custom Cards management responds to its own container", async () => {
  const cards = await css("components/custom-cards.css");
  assert.match(cards, /container:\s*custom-cards\s*\/\s*inline-size/);
  assert.match(cards, /repeat\(auto-fit,\s*minmax\(min\(12rem,\s*100%\),\s*1fr\)\)/);
  assert.match(cards, /@container\s+custom-cards\s*\(max-width:\s*42rem\)/);
  assert.doesNotMatch(cards, /(?:phone|mobile)-client|viewport-(?:portrait|landscape)/);
  assert.doesNotMatch(cards, /\.custom-card\s*\{[^}]*overflow:\s*hidden/);
});

test("Display toolbar and canvas have explicit space-driven boundaries", async () => {
  const display = await css("components/display-surface.css");
  assert.match(display, /container:\s*display-view\s*\/\s*inline-size/);
  assert.match(display, /@container\s+display-view\s*\(max-width:\s*38\.75rem\)/);
  assert.match(display, /body\.mode-display \.screen-media\s*\{[^}]*position:\s*absolute/);
  assert.match(display, /body\.viewer-fullscreen\.mode-display main\s*\{[^}]*grid-template-rows:\s*minmax\(0,\s*1fr\)/);
  assert.doesNotMatch(display, /body\.(?:phone|tablet)-client|viewport-(?:portrait|landscape)/);
});

test("Setup diagnostics grow with content inside the setup document", async () => {
  const diagnostics = await css("components/setup-diagnostics.css");
  assert.match(diagnostics, /container:\s*setup-panels\s*\/\s*inline-size/);
  assert.match(diagnostics, /\.diagnostics-panel\.is-open\s*\{[^}]*height:\s*auto/);
  assert.doesNotMatch(diagnostics, /height:\s*min\(34dvh,\s*260px\)/);
  assert.doesNotMatch(diagnostics, /diagnostics-panel-content\s*\{[^}]*overflow:\s*hidden/);
});

test("pairing layout follows its panel instead of a phone identity", async () => {
  const pairing = await css("components/pairing-setup.css");
  assert.match(pairing, /container:\s*pairing-panel\s*\/\s*inline-size/);
  assert.match(pairing, /repeat\(auto-fit,\s*minmax\(min\(100%,\s*16rem\),\s*1fr\)\)/);
  assert.match(pairing, /@container\s+pairing-panel\s*\(max-width:\s*48rem\)/);
  assert.match(pairing, /@container\s+pairing-panel\s*\(max-width:\s*30rem\)/);
  assert.doesNotMatch(pairing, /(?:phone|mobile)-client\.mode-setup/);
  assert.doesNotMatch(pairing, /new-device-panel-content\s*\{[^}]*overflow:\s*hidden/);
  assert.doesNotMatch(pairing, /#newDeviceConnectPanel\s*>\s*summary\s*\{[^}]*height:/);
});

test("shared truncation primitive does not own variable-length content", async () => {
  const shared = await css("components/shared-primitives.css");
  const truncation = shared.match(/:is\(\.brand-copy span[^}]+\{[\s\S]*?white-space:\s*nowrap;\s*\}/)?.[0] || "";
  assert.ok(truncation);
  assert.doesNotMatch(truncation, /quota|metric|feed-list|process-list|tablet-client|eink-client/);
});

test("component owners do not fork layout for a tablet identity", async () => {
  const owners = await Promise.all([
    "components/sideboard-shell.css",
    "components/metric-card.css",
    "components/quota-mini-card.css",
    "components/activity-feed.css",
    "components/quota-page.css",
    "components/display-surface.css",
    "components/custom-cards.css",
    "components/app-controls.css",
  ].map(css));
  for (const owner of owners) assert.doesNotMatch(owner, /tablet-client/);
});

test("quota account management uses one shared secondary-dialog contract instead of device variants", async () => {
  const [quota, manager, renderer, secondaryDialog, primitives] = await Promise.all([
    css("components/quota-page.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/quota-codex-account-manager.js", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/quota-card-renderer.js", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/secondary-card-dialog.js", import.meta.url), "utf8"),
    css("components/shared-primitives.css"),
  ]);
  assert.match(manager, /manager\.className\s*=\s*"quota-account-manager"/);
  assert.match(renderer, /secondary-card-dialog/);
  assert.match(renderer, /wireSecondaryCardDialog/);
  assert.match(secondaryDialog, /showModal/);
  assert.match(secondaryDialog, /hasActiveSecondaryCardInteraction/);
  assert.match(primitives, /\.secondary-card-dialog::backdrop/);
  assert.doesNotMatch(manager, /compactMobile|compactDesktop|isMobileClient/);
  assert.doesNotMatch(quota, /quota-(?:mobile|desktop)-account-manager|is-compact-(?:mobile|desktop)/);
  assert.doesNotMatch(quota, /body\.(?:phone|mobile)-client|viewport-(?:portrait|landscape)/);
});

test("component owners avoid cascade-force important declarations", async () => {
  const owners = await Promise.all([
    "10-core.css",
    "components/sideboard-shell.css",
    "components/dashboard-editor.css",
    "components/custom-cards.css",
    "components/dashboard-navigation.css",
    "components/pairing-setup.css",
    "components/setup-diagnostics.css",
    "components/device-management.css",
    "components/app-controls.css",
    "components/metric-card.css",
    "components/quota-mini-card.css",
    "components/activity-feed.css",
    "components/quota-page.css",
    "components/display-surface.css",
    "components/custom-deck.css",
    "components/eink-dashboard-content.css",
    "40-eink-sensor-orientation.css",
  ].map(css));
  for (const owner of owners) assert.doesNotMatch(owner, /!important/);
});

test("custom Deck help responds to its viewport container", async () => {
  const deck = await css("components/custom-deck.css");
  assert.match(deck, /body\.mode-deck main\s*\{[^}]*grid-template-rows:\s*minmax\(0,\s*1fr\)/);
  assert.match(deck, /\.custom-deck-view\s*\{[^}]*height:\s*auto;[^}]*align-self:\s*stretch/);
  assert.match(deck, /container:\s*custom-deck-view\s*\/\s*size/);
  assert.match(deck, /@container\s+custom-deck-view\s*\(max-width:\s*48rem\)/);
  assert.doesNotMatch(deck, /(?:phone|mobile)-client|viewport-(?:portrait|landscape)|!important/);
});

test("immersive dashboard chrome has no redundant mode switch and uses text-only connection states", async () => {
  const [html, js, navigation] = await Promise.all([
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.html", import.meta.url), "utf8"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/index.js", import.meta.url), "utf8"),
    css("components/dashboard-navigation.css"),
  ]);

  assert.doesNotMatch(html, /class="dashboard-mode-switch"/);
  assert.match(html, /data-eink-connection-state[^>]*>CONNECTING</);
  assert.match(js, /CONNECTED/);
  assert.match(js, /CONNECTING/);
  assert.match(js, /DISCONNECTED/);
  assert.doesNotMatch(navigation, /\.dashboard-mode-switch/);
  assert.match(navigation, /\.eink-connection-state[\s\S]*border:\s*0;/);
  assert.match(navigation, /\.eink-connection-state[\s\S]*background:\s*transparent;/);
});
