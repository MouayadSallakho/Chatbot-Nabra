import { expect, test as base } from '@playwright/test';

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
