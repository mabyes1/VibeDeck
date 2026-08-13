import test from "node:test";
import assert from "node:assert/strict";
import { estimateReceiverMaxBitrateKbps } from "../../src/VibeDeck.Host/wwwroot/modules/stream-controller.js";

test("ZenPad-sized Android receiver starts below its measured path ceiling", () => {
  const result = estimateReceiverMaxBitrateKbps(
    { fps: 18, quality: 48 },
    {
      navigator: { userAgent: "Mozilla/5.0 (Linux; Android 7.1.2; P024) Chrome/119" },
      screen: { width: 1280, height: 800 },
      devicePixelRatio: 1,
    });

  assert.equal(result, 1400);
});

test("desktop receivers leave the Host quality estimate uncapped", () => {
  const result = estimateReceiverMaxBitrateKbps(
    { fps: 60, quality: 80 },
    {
      navigator: { userAgent: "Mozilla/5.0 (Windows NT 10.0; Win64; x64)" },
      screen: { width: 2560, height: 1440 },
      devicePixelRatio: 1,
    });

  assert.equal(result, 0);
});
