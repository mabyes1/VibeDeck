import test from "node:test";
import assert from "node:assert/strict";
import {
  headerSpaceClasses,
  sideboardSpaceClasses,
} from "../../src/VibeDeck.Host/wwwroot/modules/responsive-space.js";

test("Sideboard space classes follow measured component width", () => {
  assert.deepEqual(sideboardSpaceClasses(280), [
    "space-sideboard-micro",
    "space-sideboard-compact",
  ]);
  assert.deepEqual(sideboardSpaceClasses(704), ["space-sideboard-compact"]);
  assert.deepEqual(sideboardSpaceClasses(911), ["space-sideboard-mid"]);
  assert.deepEqual(sideboardSpaceClasses(1_280), ["space-sideboard-wide"]);
});

test("Sideboard thresholds scale with the root font size", () => {
  assert.deepEqual(sideboardSpaceClasses(880, 20), ["space-sideboard-compact"]);
  assert.deepEqual(sideboardSpaceClasses(881, 20), ["space-sideboard-mid"]);
});

test("Header classes preserve overlapping container-query thresholds", () => {
  assert.deepEqual(headerSpaceClasses(500), [
    "space-header-max-32",
    "space-header-max-36",
    "space-header-max-48",
  ]);
  assert.deepEqual(headerSpaceClasses(911), ["space-header-min-48"]);
});
