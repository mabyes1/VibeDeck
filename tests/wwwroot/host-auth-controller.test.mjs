import assert from "node:assert/strict";
import test from "node:test";

import { buildHostAuthView } from "../../src/VibeDeck.Host/wwwroot/modules/host-auth-controller.js";

test("host auth view only shows gate when auth is required and incomplete", () => {
  assert.deepEqual(buildHostAuthView({ Required: true, Authenticated: false }), {
    required: true,
    authenticated: false,
    gateVisible: true,
  });
  assert.equal(buildHostAuthView({ required: true, authenticated: true }).gateVisible, false);
  assert.equal(buildHostAuthView({ required: false, authenticated: false }).gateVisible, false);
});
