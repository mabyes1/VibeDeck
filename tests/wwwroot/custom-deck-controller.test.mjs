import test from "node:test";
import assert from "node:assert/strict";

import { deckIdFromMode, deckMode, normalizeDeckCatalog } from "../../src/VibeDeck.Host/wwwroot/modules/custom-deck-controller.js";

test("deck mode round-trips a deck id", () => {
  assert.equal(deckMode("coding-pet"), "deck:coding-pet");
  assert.equal(deckIdFromMode("deck:coding-pet"), "coding-pet");
  assert.equal(deckIdFromMode("quota"), "");
});

test("catalog normalization accepts Host PascalCase payload", () => {
  const catalog = normalizeDeckCatalog({
    RootPath: "C:\\ProgramData\\VibeDeck\\Decks",
    Decks: [{ Id: "coding-pet", Name: "Coding Pet", Entry: "index.html", Icon: "🐱", Url: "/decks/coding-pet/index.html" }],
    Issues: [{ Folder: "broken", Message: "deck.json is invalid" }],
  });
  assert.equal(catalog.rootPath, "C:\\ProgramData\\VibeDeck\\Decks");
  assert.equal(catalog.decks[0].id, "coding-pet");
  assert.equal(catalog.decks[0].url, "/decks/coding-pet/index.html");
  assert.deepEqual(catalog.issues[0], { folder: "broken", message: "deck.json is invalid" });
});

test("catalog drops unusable deck records", () => {
  const catalog = normalizeDeckCatalog({ decks: [
    { id: "ok", name: "OK", url: "/decks/ok/index.html" },
    { id: "missing-url", name: "Broken" },
  ] });
  assert.deepEqual(catalog.decks.map(deck => deck.id), ["ok"]);
});
