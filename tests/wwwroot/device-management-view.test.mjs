import assert from "node:assert/strict";
import test from "node:test";

import {
  formatDeviceTime,
  readDeviceField,
} from "../../src/VibeDeck.Host/wwwroot/modules/device-management-view.js";

test("device field reader supports API casing variants", () => {
  assert.equal(readDeviceField({ DeviceId: "pascal" }, "DeviceId", "deviceId"), "pascal");
  assert.equal(readDeviceField({ DeviceId: "pascal", deviceId: "camel" }, "DeviceId", "deviceId"), "pascal");
});

test("device time fails closed for missing and invalid timestamps", () => {
  assert.equal(formatDeviceTime("", "en-US"), "--");
  assert.equal(formatDeviceTime("invalid", "en-US"), "--");
  assert.notEqual(formatDeviceTime("2026-08-09T00:00:00Z", "en-US"), "--");
});
