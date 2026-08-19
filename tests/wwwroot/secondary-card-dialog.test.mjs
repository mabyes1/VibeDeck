import assert from "node:assert/strict";
import test from "node:test";
import { hasActiveSecondaryCardInteraction } from "../../src/VibeDeck.Host/wwwroot/modules/secondary-card-dialog.js";

test("secondary-card interaction protects an open dialog from background rerenders", () => {
  const root = {
    querySelector: selector => selector === ".secondary-card-dialog[open]" ? {} : null,
    contains: () => false,
  };
  assert.equal(hasActiveSecondaryCardInteraction(root, { activeElement: null }), true);
});

test("secondary-card interaction protects a focused native select", () => {
  const activeElement = { matches: selector => selector === "select" };
  const root = {
    querySelector: () => null,
    contains: element => element === activeElement,
  };
  assert.equal(hasActiveSecondaryCardInteraction(root, { activeElement }), true);
  assert.equal(hasActiveSecondaryCardInteraction(root, { activeElement: null }), false);
});
