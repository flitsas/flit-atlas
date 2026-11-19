import { defineConfig, devices } from '@playwright/test';

/**
 * E2E contra Atlas levantado con el AppHost de Aspire (`dotnet run --project aspire/Flit.Atlas.AppHost`).
 * Perfiles del prompt maestro §13: escritorio, Pixel 7 e iPhone 14.
 */
export default defineConfig({
  testDir: './tests/e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.ATLAS_E2E_URL ?? 'https://localhost:4032',
    // El certificado de desarrollo de .NET no está en el almacén de los navegadores de Playwright.
    ignoreHTTPSErrors: !process.env.ATLAS_E2E_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },
  projects: [
    { name: 'escritorio', use: { ...devices['Desktop Chrome'] } },
    { name: 'pixel-7', use: { ...devices['Pixel 7'] } },
    { name: 'iphone-14', use: { ...devices['iPhone 14'] } },
  ],
});
