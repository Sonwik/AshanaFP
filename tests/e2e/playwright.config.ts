import { defineConfig, devices } from '@playwright/test';

/**
 * End-to-end tests run the real app in a real browser.
 * By default Playwright starts the app itself (in-memory demo data, fresh on every run).
 * Set BASE_URL to test an app that is already running instead, for example BASE_URL=http://localhost:5191.
 */
const port = Number(process.env.ASHANA_PORT ?? 5199);
const baseURL = process.env.BASE_URL ?? `http://127.0.0.1:${port}`;

export default defineConfig({
  testDir: './specs',
  timeout: 60_000,
  expect: { timeout: 10_000 },
  // The specs share one server and its data (orders placed by one test are visible to the next), so run them in order.
  fullyParallel: false,
  workers: 1,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'phone', use: { ...devices['Pixel 7'] }, testMatch: /(smoke|layout|accessibility)\.spec\.ts/ },
  ],
  webServer: process.env.BASE_URL
    ? undefined
    : {
        command:
          `dotnet run --project ../../src/Ashana.Web --launch-profile http -- --urls ${baseURL} ` +
          '--Storage:Provider=Memory --Worker:Enabled=false --RateLimits:AuthPerMinute=1000',
        url: `${baseURL}/health`,
        reuseExistingServer: !process.env.CI,
        timeout: 180_000,
      },
});
