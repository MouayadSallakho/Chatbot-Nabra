import { expect, signedInTest as test } from './support/fixtures';

test('the widget settings offer corners that follow the page, drawn for the reader', async ({
  page,
  appLanguage,
}) => {
  await page.goto('/channels/website-chat');
  await page.locator('a[href*="/channels/website-chat/"]').first().click();

  // The corners are four radio cards in one group (Phase 39.5b), still under #widget-position.
  const corner = page.locator('#widget-position');
  const launcher = page.getByTestId('widget-preview').locator('app-chat-launcher');

  await expect(corner.getByRole('radio')).toHaveCount(4);
  await expect(page.getByTestId('visitors-see-name')).toBeVisible();

  // Nothing is saved: the preview follows the form.
  await corner.locator('input[value="Start"]').check();
  await expect(launcher).toHaveClass(appLanguage === 'ar' ? /^(?!.*is-left)/ : /is-left/);

  await corner.locator('input[value="End"]').check();
  await expect(launcher).toHaveClass(appLanguage === 'ar' ? /is-left/ : /^(?!.*is-left)/);
});
