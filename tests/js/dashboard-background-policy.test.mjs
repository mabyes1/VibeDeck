import test from "node:test";
import assert from "node:assert/strict";
import {
  isFullscreenDisplayStreaming,
  shouldRunDashboardBackgroundWork,
} from "../../src/VibeDeck.Host/wwwroot/modules/dashboard-background-policy.js";

function bodyWith(...classes) {
  return { classList: { contains: value => classes.includes(value) } };
}

test("fullscreen display pauses dashboard background work", () => {
  const body = bodyWith("viewer-fullscreen");
  assert.equal(isFullscreenDisplayStreaming("display", body), true);
  assert.equal(shouldRunDashboardBackgroundWork("display", body), false);
});

test("dashboard viewer and normal display keep their own refresh path", () => {
  assert.equal(isFullscreenDisplayStreaming("sideboard", bodyWith("viewer-fullscreen")), false);
  assert.equal(shouldRunDashboardBackgroundWork("sideboard", bodyWith("dashboard-viewer")), true);
  assert.equal(shouldRunDashboardBackgroundWork("display", bodyWith()), true);
});

test("hidden documents never run background work", () => {
  assert.equal(shouldRunDashboardBackgroundWork("sideboard", bodyWith(), "hidden"), false);
});
