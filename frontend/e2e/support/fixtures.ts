import { Browser, Page, expect, test as base } from '@playwright/test';
import { join } from 'node:path';
import { BASE_URL } from './target';

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

/**
 * The public key of the run's second website chat, kept for the journeys where a
 * visitor waits for a reply or a person steps in (four new conversations a run).
 */
export function e2eHandoverChatKey(): string {
  const key = process.env['NABRA_E2E_HANDOVER_CHAT_KEY'];

  if (!key) {
    throw new Error('Global setup did not provide the second website chat.');
  }

  return key;
}

/**
 * A visitor of the run's website chat, in a browser of their own: no session, no
 * stored language, only the link's `?lang`, as somebody arriving from a website.
 */
export async function openVisitor(
  browser: Browser,
  chatKey: string,
  language: 'en' | 'ar',
): Promise<{ visitor: Page; close: () => Promise<void> }> {
  const context = await within(
    browser.newContext({ baseURL: BASE_URL, locale: language === 'ar' ? 'ar-SA' : 'en-US' }),
    'opening the visitor',
  );
  const close = () => closeWithin(context);

  try {
    // `newPage` takes no timeout of its own: a browser that has stopped answering
    // would otherwise hold the journey past its own limit.
    const visitor = await within(context.newPage(), 'opening the visitor page');

    await visitor.goto(`/chat/${chatKey}?lang=${language}`, { timeout: STEP_MS });
    await expect(visitor.locator('html')).toHaveAttribute('dir', directionOf(language));

    return { visitor, close };
  } catch (error) {
    await close();
    throw error;
  }
}

/**
 * The longest any single step of a journey may take (Phase 38.8). A journey's own
 * limit is 60 seconds; a step that hangs must fail well inside it.
 */
export const STEP_MS = 15_000;

/**
 * Settles a browser call that has no timeout of its own within `STEP_MS`, or fails
 * the step by name. Nothing a journey awaits may wait without a bound: in 38.7b one
 * worker sat in `slow-reply` for 1.9 hours on an unbounded close after a failure.
 */
export async function within<T>(work: Promise<T>, what: string, ms = STEP_MS): Promise<T> {
  let timer: ReturnType<typeof setTimeout> | undefined;

  try {
    return await Promise.race([
      work,
      new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new Error(`${what} took longer than ${ms} ms`)), ms);
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

/**
 * Closes a context the journey opened itself, waiting at most `STEP_MS`.
 *
 * A close is clean-up, run from `finally` after a failure too: if the browser has
 * stopped answering, the journey ends anyway and reports the failure it had, and the
 * worker's own browser is closed with the worker.
 */
export async function closeWithin(context: { close(): Promise<void> }): Promise<void> {
  await within(context.close(), 'closing a browser context').catch(() => undefined);
}

/** The run's conversation that is longer than one window. */
export function e2eLongConversation(): string {
  const id = process.env['NABRA_E2E_LONG_CONVERSATION'];

  if (!id) {
    throw new Error('Global setup did not provide the long conversation.');
  }

  return id;
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
