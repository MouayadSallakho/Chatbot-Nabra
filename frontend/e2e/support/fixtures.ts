import { expect, test as base } from '@playwright/test';
import { join } from 'node:path';

/** Set per project: `locale` sets `Intl`, not the language the app is using. */
export interface AppLanguageOption {
  readonly appLanguage: 'en' | 'ar';
}

/** Every journey runs in its project's language, stored the way the app stores it. */
export const test = base.extend<AppLanguageOption>({
  appLanguage: ['en', { option: true }],
  page: async ({ page, appLanguage }, use) => {
    await page.addInitScript((language: string) => {
      localStorage.setItem('omniai.lang', language);
    }, appLanguage);

    await use(page);
  },
});

export { expect };

/** A journey that starts signed in as the run's owner. */
export const signedInTest = test.extend({
  storageState: async ({}, use) => {
    const stateDir = process.env['NABRA_E2E_STATE_DIR'];

    if (!stateDir) {
      throw new Error('Global setup did not provide the saved session.');
    }

    await use(join(stateDir, 'owner.json'));
  },
});

/** The run's workspace, and the key of its website chat. */
export function e2eWorkspace(): { tenantId: string; chatKey: string } {
  const tenantId = process.env['NABRA_E2E_TENANT_ID'];
  const chatKey = process.env['NABRA_E2E_CHAT_KEY'];

  if (!tenantId || !chatKey) {
    throw new Error('Global setup did not provide the workspace.');
  }

  return { tenantId, chatKey };
}

/** The direction a page in this language must have. */
export function directionOf(language: 'en' | 'ar'): 'ltr' | 'rtl' {
  return language === 'ar' ? 'rtl' : 'ltr';
}

/** The run's synthetic account: created by global setup, held in memory only. */
export function e2eAccount(): { email: string; password: string; throttleEmail: string } {
  const email = process.env['NABRA_E2E_EMAIL'];
  const password = process.env['NABRA_E2E_PASSWORD'];
  const throttleEmail = process.env['NABRA_E2E_THROTTLE_EMAIL'];

  if (!email || !password || !throttleEmail) {
    throw new Error('Global setup did not provide the run account.');
  }

  return { email, password, throttleEmail };
}
