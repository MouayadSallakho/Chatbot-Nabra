import { defineConfig } from '@playwright/test';
import { join } from 'node:path';
import { AppLanguageOption } from './e2e/support/fixtures';
import { BASE_URL } from './e2e/support/target';

/** Where results and failure artifacts go: outside the repository. */
const OUT = process.env['NABRA_E2E_OUT'] ?? 'node_modules/.cache/nabra-e2e';

/**
 * End-to-end journeys against the running dev stack, on the installed Chrome.
 *
 * One worker and no retries: the journeys share one API and its limits, and a
 * journey that only passes on a second try is a defect to fix. Run alone, after
 * the provider canary (`docs/phase-reports/PHASE_38_0_FRONTEND_PLAN_REPORT.md`, §3.12).
 */
export default defineConfig<AppLanguageOption>({
  testDir: './e2e',
  testMatch: '**/*.e2e.ts',
  workers: 1,
  fullyParallel: false,
  retries: 0,
  timeout: 60_000,
  forbidOnly: true,
  globalSetup: './e2e/support/global-setup.ts',
  globalTeardown: './e2e/support/global-teardown.ts',
  outputDir: join(OUT, 'artifacts'),
  reporter: [['list'], ['json', { outputFile: join(OUT, 'e2e-results.json') }]],
  use: {
    baseURL: BASE_URL,
    channel: 'chrome',
    headless: true,
    trace: 'off',
    screenshot: 'only-on-failure',
  },
  // No journey depends on another or on this order (38.6): each one sets up, or
  // puts back, the state it needs.
  projects: [
    { name: 'en-ltr', use: { locale: 'en-US', appLanguage: 'en' } },
    { name: 'ar-rtl', use: { locale: 'ar-SA', appLanguage: 'ar' } },
  ],
});
