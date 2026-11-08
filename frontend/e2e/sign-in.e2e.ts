import { directionOf, e2eAccount, expect, test } from './support/fixtures';

test('signing in opens the workspace', async ({ page, appLanguage }) => {
  const { email, password } = e2eAccount();

  await page.goto('/sign-in');

  await expect(page.locator('html')).toHaveAttribute('lang', appLanguage);
  await expect(page.locator('html')).toHaveAttribute('dir', directionOf(appLanguage));

  await page.locator('#email').fill(email);
  await page.locator('#password').fill(password);
  await page.locator('button[type="submit"]').click();

  await expect(page).toHaveURL(/\/overview$/);
  await expect(page.locator('h1')).toHaveCount(1);
});
