import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

const css = fs.readFileSync(new URL("../../src/VibeDeck.Host/wwwroot/css/components/app-controls.css", import.meta.url), "utf8");
const core = fs.readFileSync(new URL("../../src/VibeDeck.Host/wwwroot/css/10-core.css", import.meta.url), "utf8");

test("header gives flexible width to the two-row control area", () => {
  assert.match(core, /grid-template-columns:\s*auto minmax\(0, 1fr\)/);
  assert.match(css, /\.top-controls\s*\{[\s\S]*grid-template-rows:\s*auto auto/);
});

test("dynamic deck navigation stays one horizontal scroll row", () => {
  assert.match(css, /\.view-switcher\s*\{[\s\S]*display:\s*flex;[\s\S]*flex-wrap:\s*nowrap;[\s\S]*overflow-x:\s*auto/);
  assert.match(css, /\.view-switcher \.custom-deck-tab\s*\{[\s\S]*max-width:\s*190px;[\s\S]*text-overflow:\s*ellipsis/);
});

test("utility controls use their own non-wrapping horizontal row", () => {
  assert.match(css, /\.utility-controls\s*\{[\s\S]*display:\s*flex;[\s\S]*flex-wrap:\s*nowrap;[\s\S]*overflow-x:\s*auto/);
});
