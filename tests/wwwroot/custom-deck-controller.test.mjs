import test from "node:test";
import assert from "node:assert/strict";

import { deckIdFromMode, deckMode, deckSandbox, normalizeDeckCatalog } from "../../src/VibeDeck.Host/wwwroot/modules/custom-deck-controller.js";

test("deck mode round-trips a deck id", () => {
  assert.equal(deckMode("coding-pet"), "deck:coding-pet");
  assert.equal(deckIdFromMode("deck:coding-pet"), "coding-pet");
  assert.equal(deckIdFromMode("quota"), "");
});

test("catalog normalization accepts Host PascalCase payload", () => {
  const catalog = normalizeDeckCatalog({
    RootPath: "C:\\ProgramData\\VibeDeck\\Decks",
    Decks: [{ Id: "coding-pet", Name: "Coding Pet", Entry: "index.html", Icon: "🐱", Type: "static", Url: "/decks/coding-pet/index.html" }],
    Issues: [{ Folder: "broken", Message: "deck.json is invalid" }],
  });
  assert.equal(catalog.rootPath, "C:\\ProgramData\\VibeDeck\\Decks");
  assert.equal(catalog.decks[0].id, "coding-pet");
  assert.equal(catalog.decks[0].url, "/decks/coding-pet/index.html");
  assert.equal(catalog.decks[0].type, "static");
  assert.deepEqual(catalog.issues[0], { folder: "broken", message: "deck.json is invalid" });
});

test("catalog normalization preserves proxy deck type", () => {
  const catalog = normalizeDeckCatalog({ decks: [
    { id: "internal-monitor", name: "Internal Monitor", type: "proxy", url: "/deck-proxy/internal-monitor/" },
  ] });
  assert.equal(catalog.decks[0].type, "proxy");
});

test("embed deck receives direct-site sandbox capabilities", () => {
  assert.equal(
    deckSandbox("embed", "http://10.0.0.42:8666/", "http://10.0.0.42:5000"),
    "allow-scripts allow-forms allow-same-origin",
  );
});

test("same-origin embed does not receive allow-same-origin", () => {
  assert.equal(
    deckSandbox("embed", "http://10.0.0.42:5000/internal", "http://10.0.0.42:5000"),
    "allow-scripts allow-forms",
  );
});

test("catalog drops unusable deck records", () => {
  const catalog = normalizeDeckCatalog({ decks: [
    { id: "ok", name: "OK", url: "/decks/ok/index.html" },
    { id: "missing-url", name: "Broken" },
  ] });
  assert.deepEqual(catalog.decks.map(deck => deck.id), ["ok"]);
});
