import assert from "node:assert/strict";
import test from "node:test";

import {
  chooseDisplay,
  normalizeDisplay,
} from "../../src/VibeDeck.Host/wwwroot/modules/display-source-controller.js";

test("display normalization accepts PascalCase and camelCase payloads", () => {
  assert.deepEqual(normalizeDisplay({
    deviceName: "DISPLAY2",
    friendlyName: "Monitor",
    deviceId: "id-2",
    outputIndex: "1",
    width: "1920",
    height: 1080,
    isPrimary: true,
    isVibeDeckDisplay: false,
  }), {
    DeviceName: "DISPLAY2",
    FriendlyName: "Monitor",
    DeviceId: "id-2",
    OutputIndex: 1,
    Width: 1920,
    Height: 1080,
    IsPrimary: true,
    IsVibeDeckDisplay: false,
  });
});

test("display choice preserves stored identity before transient or fallback choices", () => {
  const displays = [
    { DeviceName: "PRIMARY", DeviceId: "p", IsPrimary: true, IsVibeDeckDisplay: false },
    { DeviceName: "VIRTUAL", DeviceId: "v", IsPrimary: false, IsVibeDeckDisplay: true },
    { DeviceName: "OTHER", DeviceId: "o", IsPrimary: false, IsVibeDeckDisplay: false },
  ];

  assert.equal(chooseDisplay(displays, { deviceId: "o" }, "VIRTUAL").DeviceName, "OTHER");
  assert.equal(chooseDisplay(displays, { deviceName: "PRIMARY" }, "VIRTUAL").DeviceName, "PRIMARY");
});

test("display choice falls back through transient, virtual, primary, then first", () => {
  const displays = [
    { DeviceName: "PRIMARY", IsPrimary: true, IsVibeDeckDisplay: false },
    { DeviceName: "VIRTUAL", IsPrimary: false, IsVibeDeckDisplay: true },
    { DeviceName: "OTHER", IsPrimary: false, IsVibeDeckDisplay: false },
  ];

  assert.equal(chooseDisplay(displays, {}, "OTHER").DeviceName, "OTHER");
  assert.equal(chooseDisplay(displays, {}, "").DeviceName, "VIRTUAL");
  assert.equal(chooseDisplay(displays.filter(item => !item.IsVibeDeckDisplay), {}, "").DeviceName, "PRIMARY");
  assert.equal(chooseDisplay([{ DeviceName: "ONLY" }], {}, "").DeviceName, "ONLY");
  assert.equal(chooseDisplay([], {}, ""), null);
});
