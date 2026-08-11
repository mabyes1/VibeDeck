import assert from "node:assert/strict";
import test from "node:test";

import { formatVerificationCode } from "../../src/VibeDeck.Host/wwwroot/modules/device-actions-controller.js";

test("verification code groups six digits for visual confirmation", () => {
  assert.equal(formatVerificationCode("123456"), "123 456");
  assert.equal(formatVerificationCode("12-34 56"), "123 456");
});

test("verification code preserves non-six-digit input after normalization", () => {
  assert.equal(formatVerificationCode("1234"), "1234");
  assert.equal(formatVerificationCode(""), "------");
});
