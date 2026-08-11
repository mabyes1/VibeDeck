import assert from "node:assert/strict";
import test from "node:test";

import { buildProductUpdateView } from "../../src/VibeDeck.Host/wwwroot/modules/product-update-controller.js";

const t = (key, values = {}) => `${key}${values.version ? `:${values.version}` : ""}${values.percent != null ? `:${values.percent}` : ""}`;

test("available product update exposes install action without busy state", () => {
  const view = buildProductUpdateView({ State: "available", Code: "available", LatestVersion: "2.0.0", CanStart: true }, t);

  assert.equal(view.state, "available");
  assert.equal(view.canStart, true);
  assert.equal(view.busy, false);
  assert.equal(view.buttonText, "updates.install:2.0.0");
  assert.equal(view.feedbackState, "info");
});

test("downloading update is busy and reports percentage", () => {
  const view = buildProductUpdateView({ state: "downloading", code: "downloading", downloadPercent: 42 }, t);

  assert.equal(view.busy, true);
  assert.equal(view.message, "updates.downloading:42");
  assert.equal(view.statusMessage, "updates.statusDownloading:42");
  assert.equal(view.feedbackState, "working");
});

test("failed and current update states preserve feedback semantics", () => {
  assert.equal(buildProductUpdateView({ state: "failed", code: "update_failed" }, t).feedbackState, "error");
  assert.equal(buildProductUpdateView({ state: "current", code: "current" }, t).feedbackState, "success");
  assert.equal(buildProductUpdateView({ state: "idle", code: "idle" }, t).feedbackState, "muted");
});
