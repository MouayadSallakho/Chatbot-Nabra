import { expect, signedInTest as test } from './support/fixtures';

test('the widget settings offer corners that follow the page, drawn for the reader', async ({
  page,
  appLanguage,
}) => {
  await page.goto('/channels/website-chat');
  await page.locator('a[href*="/channels/website-chat/"]').first().click();

  const corner = page.locator('#widget-position');
  const launcher = page.getByTestId('widget-preview').locator('app-chat-launcher');

  await expect(corner.locator('option')).toHaveCount(4);
  await expect(page.getByTestId('visitors-see-name')).toBeVisible();

  // Nothing is saved: the preview follows the form.
  await corner.selectOption('Start');
  await expect(launcher).toHaveClass(appLanguage === 'ar' ? /^(?!.*is-left)/ : /is-left/);

  await corner.selectOption('End');
  await expect(launcher).toHaveClass(appLanguage === 'ar' ? /is-left/ : /^(?!.*is-left)/);
});
