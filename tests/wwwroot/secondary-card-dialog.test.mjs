import assert from "node:assert/strict";
import test from "node:test";
import {
  hasActiveSecondaryCardInteraction,
  onSecondaryCardInteractionEnd,
} from "../../src/VibeDeck.Host/wwwroot/modules/secondary-card-dialog.js";

test("secondary-card interaction protects an open dialog from background rerenders", () => {
  const root = {
    querySelector: selector => selector === ".secondary-card-dialog[open]" ? {} : null,
    contains: () => false,
  };
  assert.equal(hasActiveSecondaryCardInteraction(root, { activeElement: null }), true);
});

test("focused native select does not permanently block background refresh", () => {
  const activeElement = { matches: selector => selector === "select" };
  const root = {
    querySelector: () => null,
    contains: element => element === activeElement,
  };
  assert.equal(hasActiveSecondaryCardInteraction(root, { activeElement }), false);
  assert.equal(hasActiveSecondaryCardInteraction(root, { activeElement: null }), false);
});

test("deferred refresh resumes as soon as the open dialog closes", () => {
  let closeHandler = null;
  let calls = 0;
  const dialog = {
    addEventListener(type, handler, options) {
      assert.equal(type, "close");
      assert.deepEqual(options, { once: true });
      closeHandler = handler;
    },
  };
  const root = {
    querySelector: selector => selector === ".secondary-card-dialog[open]" ? dialog : null,
  };

  assert.equal(onSecondaryCardInteractionEnd(root, () => { calls += 1; }), true);
  assert.equal(calls, 0);
  closeHandler();
  assert.equal(calls, 1);
});
