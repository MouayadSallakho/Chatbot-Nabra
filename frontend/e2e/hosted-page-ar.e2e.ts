import { e2eWorkspace, expect, test } from './support/fixtures';

/**
 * The visitor's language comes from the link. Both projects run these, and the
 * fixture stores the project's language the way an admin's browser would, so
 * each also proves a visitor's page does not read that stored choice.
 *
 * Nothing here sends a message: the suite is exactly at the public chat's limit.
 */
test('?lang=ar opens the hosted page in Arabic, right to left from the first paint', async ({
  page,
}) => {
  const { chatKey } = e2eWorkspace();

  await page.goto(`/chat/${chatKey}?lang=ar`, { waitUntil: 'commit' });
  await page.waitForFunction(() => document.documentElement.lang !== '');

  await expect(page.locator('html')).toHaveAttribute('lang', 'ar');
  await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');

  await expect(page.getByTestId('chat-input')).toHaveAttribute('placeholder', 'اكتب رسالتك…');
  await expect(page.getByTestId('chat-send')).toHaveText('إرسال');
  await expect(page.locator('.chat-honesty')).toContainText('الإجابات يولّدها الذكاء الاصطناعي.');
  await expect(page.locator('h1')).toHaveCount(1);
  await expect(page.getByTestId('chat-title')).toContainText('E2E');
});

test('?lang=en opens the hosted page in English, left to right', async ({ page }) => {
  const { chatKey } = e2eWorkspace();

  await page.goto(`/chat/${chatKey}?lang=en`);

  await expect(page.locator('html')).toHaveAttribute('lang', 'en');
  await expect(page.locator('html')).toHaveAttribute('dir', 'ltr');
  await expect(page.getByTestId('chat-input')).toHaveAttribute('placeholder', 'Type your message…');
  await expect(page.getByTestId('chat-send')).toHaveText('Send');
});
