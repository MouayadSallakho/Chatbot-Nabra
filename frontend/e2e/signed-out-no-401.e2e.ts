import { expect, test } from './support/fixtures';

const SIGNED_OUT_PAGES = [
  '/sign-in',
  '/create-account',
  '/forgot-password',
  '/reset-password',
  '/accept-invitation',
];

for (const path of SIGNED_OUT_PAGES) {
  test(`${path} asks about the session without a 401`, async ({ page }) => {
    const refused: string[] = [];
    const errors: string[] = [];
    const sessionAnswers: number[] = [];
    let askedMe = false;

    page.on('response', (response) => {
      const url = new URL(response.url());

      if (response.status() === 401) {
        refused.push(url.pathname);
      }
      if (url.pathname === '/api/auth/session') {
        sessionAnswers.push(response.status());
      }
      if (url.pathname === '/api/auth/me') {
        askedMe = true;
      }
    });
    page.on('console', (message) => {
      if (message.type() === 'error') {
        errors.push(message.text());
      }
    });

    await page.goto(path);
    await expect(page.locator('h1')).toHaveCount(1);

    expect(sessionAnswers).toEqual([200]);
    expect(askedMe).toBe(false);
    expect(refused).toEqual([]);
    expect(errors).toEqual([]);
  });
}
