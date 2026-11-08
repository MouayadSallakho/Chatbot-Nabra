import { Page, Response } from '@playwright/test';
import { e2eAccount, expect, test } from './support/fixtures';

/**
 * Being refused for too many attempts, against the real limiter.
 *
 * The address is random and has no account. The English project spends the six
 * failures (the limit is 5 per address and network in 15 minutes); the Arabic one
 * reuses that address and is refused at once, adding none.
 */
const WRONG_PASSWORD = 'not-the-password-of-anybody';

const WORDS = {
  en: /Too many attempts\. Try again in (\d+) minutes?\./,
  ar: /محاولات كثيرة جدًا\. حاول مرة أخرى بعد (.+)\./,
};

async function attempt(page: Page, email: string): Promise<Response> {
  await page.locator('#email').fill(email);
  await page.locator('#password').fill(WRONG_PASSWORD);

  const [answer] = await Promise.all([
    page.waitForResponse((response) => new URL(response.url()).pathname === '/api/auth/login'),
    page.locator('button[type="submit"]').click(),
  ]);

  return answer;
}

test('too many failed sign-ins say when to try again and hold the button', async ({
  page,
  appLanguage,
}, testInfo) => {
  const { throttleEmail } = e2eAccount();

  await page.goto('/sign-in');

  const attempts = appLanguage === 'en' ? 6 : 1;
  let answer: Response | undefined;

  for (let tried = 0; tried < attempts && answer?.status() !== 429; tried++) {
    answer = await attempt(page, throttleEmail);
  }

  expect(answer?.status()).toBe(429);

  const retryAfter = Number(answer?.headers()['retry-after']);
  const failure = page.getByTestId('sign-in-failure');

  expect(retryAfter).toBeGreaterThan(0);
  await expect(failure).toContainText(WORDS[appLanguage]);
  await expect(page.locator('button[type="submit"]')).toBeDisabled();
  await expect(page.getByTestId('sign-in-wait-announcer')).toContainText(WORDS[appLanguage]);

  if (appLanguage === 'en') {
    await expect(failure).toContainText(`${Math.ceil(retryAfter / 60)} minute`);
  }

  // Kept with the run's results: the wording and the wait, for the report.
  await testInfo.attach('throttled-wording', {
    body: `${(await failure.innerText()).trim()} | Retry-After: ${retryAfter}`,
    contentType: 'text/plain',
  });
  await page.screenshot({ path: testInfo.outputPath('throttled.png') });
});
