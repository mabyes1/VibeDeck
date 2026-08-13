import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const cssRoot = new URL("../../src/VibeDeck.Host/wwwroot/css/", import.meta.url);

async function css(path) {
  return readFile(new URL(path, cssRoot), "utf8");
}

test("compact Sideboard is selected by container space instead of a device selector", async () => {
  const [phone, shell, compatibility] = await Promise.all([
    css("30-phone-dashboard.css"),
    css("components/sideboard-shell.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/responsive-space.js", import.meta.url), "utf8"),
  ]);

  assert.match(shell, /container:\s*sideboard-view\s*\/\s*inline-size/);
  assert.match(shell, /\.sideboard-shell\s*\{[\s\S]*min-width:\s*0/);
  assert.match(phone, /@container\s+sideboard-view\s*\(max-width:\s*44rem\)/);
  assert.match(phone, /@container\s+sideboard-view\s*\(max-width:\s*18rem\)[\s\S]*\.mobile-overview-actions[\s\S]*repeat\(2,\s*minmax\(0,\s*1fr\)\)/);
  assert.match(shell, /@container sideboard-view[^}]*[\s\S]*\.sideboard-shell\s*\{[^}]*scrollbar-gutter:\s*auto/);
  assert.match(phone, /space-sideboard-compact[^}]*\.mobile-overview\s*\{\s*display:\s*flex/);
  assert.match(shell, /space-sideboard-mid[\s\S]*grid-template-columns:\s*minmax\(0,\s*1fr\)\s+auto\s+auto/);
  assert.match(compatibility, /ResizeObserver/);
  assert.match(compatibility, /sideboardSpaceClasses/);
  assert.doesNotMatch(phone, /@media\s*\(max-width:\s*1100px\)/);
  assert.doesNotMatch(phone, /phone-client|tablet-client|viewport-(?:portrait|landscape)/);
});

test("dashboard editor is one content-sized scrolling workspace", async () => {
  const editor = await css("components/dashboard-editor.css");

  assert.match(editor, /body\.dashboard-edit-mode\s*\{[\s\S]*overflow-y:\s*auto/);
  assert.match(editor, /min-height:\s*calc\(var\(--viewer-height,\s*100vh\)\s*-\s*var\(--dashboard-editor-chrome/);
  assert.doesNotMatch(editor, /min-height:\s*(?:820|720|520)px/);
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
  assert.match(editor, /repeat\(var\(--dashboard-row-count,\s*6\)/);
  assert.match(controller, /grid\.style\.setProperty\("--dashboard-row-count",\s*String\(maxRows\(\)\)\)/);
  assert.match(controller, /node\.hidden\s*=\s*!visible/);
  assert.doesNotMatch(controller, /dashboard-card-hidden/);
  assert.doesNotMatch(shell, /viewport-portrait\s+#systemSideboardPage\.dashboard-grid/);
  assert.doesNotMatch(controller, /isTabletClient|tablet-(?:portrait|landscape)/);
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
  assert.doesNotMatch(core, /body\.phone-client|body\.tablet-client|viewport-(?:portrait|landscape)/);
  assert.doesNotMatch(core, /@media\s*\((?:max|min)-(?:width|height)/);
});

test("logical landscape chrome consumes one authoritative state class", async () => {
  const [core, controls, shell, quota] = await Promise.all([
    css("10-core.css"),
    css("components/app-controls.css"),
    css("components/sideboard-shell.css"),
    css("components/quota-page.css"),
  ]);
  assert.match(core, /html\.phone-force-landscape body\.force-landscape\s*\{/);
  for (const owner of [controls, shell, quota]) {
    assert.doesNotMatch(owner, /html\.phone-force-landscape body\.force-landscape/);
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
});

test("Custom Cards management responds to its own container", async () => {
  const cards = await css("components/custom-cards.css");
  assert.match(cards, /container:\s*custom-cards\s*\/\s*inline-size/);
  assert.match(cards, /repeat\(auto-fit,\s*minmax\(min\(12rem,\s*100%\),\s*1fr\)\)/);
  assert.match(cards, /@container\s+custom-cards\s*\(max-width:\s*42rem\)/);
  assert.doesNotMatch(cards, /phone-client|viewport-(?:portrait|landscape)/);
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
  assert.doesNotMatch(pairing, /phone-client\.mode-setup/);
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

test("quota account management uses one DOM contract instead of device variants", async () => {
  const [quota, manager] = await Promise.all([
    css("components/quota-page.css"),
    readFile(new URL("../../src/VibeDeck.Host/wwwroot/modules/quota-codex-account-manager.js", import.meta.url), "utf8"),
  ]);
  assert.match(manager, /manager\.className\s*=\s*"quota-account-manager"/);
  assert.doesNotMatch(manager, /compactMobile|compactDesktop|isMobileClient/);
  assert.doesNotMatch(quota, /quota-(?:mobile|desktop)-account-manager|is-compact-(?:mobile|desktop)/);
  assert.doesNotMatch(quota, /body\.phone-client|viewport-(?:portrait|landscape)/);
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
  assert.doesNotMatch(deck, /phone-client|viewport-(?:portrait|landscape)|!important/);
});
