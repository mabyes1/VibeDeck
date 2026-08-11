import assert from "node:assert/strict";
import test from "node:test";

import {
  buildDisplayInstallView,
  readDisplayInstallField,
} from "../../src/VibeDeck.Host/wwwroot/modules/display-install-controller.js";

const tApi = (code, message) => code ? `api:${code}` : message;

test("display install field reader accepts API casing variants", () => {
  assert.equal(readDisplayInstallField({ State: "installed" }, "State", "ready"), "installed");
  assert.equal(readDisplayInstallField({ state: "installing" }, "State", "ready"), "installing");
  assert.equal(readDisplayInstallField({}, "State", "ready"), "ready");
});

test("display install view maps lifecycle states to feedback semantics", () => {
  assert.equal(buildDisplayInstallView({ State: "installing" }, tApi).feedbackState, "working");
  assert.equal(buildDisplayInstallView({ State: "installed" }, tApi).feedbackState, "success");
  assert.equal(buildDisplayInstallView({ State: "restart-required" }, tApi).feedbackState, "warning");
  assert.equal(buildDisplayInstallView({ State: "failed" }, tApi).feedbackState, "error");
});

test("display install view preserves localized API message and can-install flag", () => {
  const view = buildDisplayInstallView({ Code: "driver_missing", Message: "fallback", CanInstall: true }, tApi);

  assert.equal(view.localizedMessage, "api:driver_missing");
  assert.equal(view.canInstall, true);
});
