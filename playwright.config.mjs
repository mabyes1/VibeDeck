import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./tests/visual",
  outputDir: "./test-results",
  fullyParallel: false,
  workers: 1,
  reporter: "line",
  use: {
    channel: "chrome",
    headless: true,
    trace: "retain-on-failure",
  },
});
