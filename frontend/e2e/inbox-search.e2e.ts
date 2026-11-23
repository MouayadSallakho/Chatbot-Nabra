import { e2eLongConversation, expect, signedInTest as test } from './support/fixtures';

test('finds an older message, clears a miss and keeps the inbox usable on a narrow screen', async ({
  page,
  appLanguage,
}) => {
  await page.setViewportSize({ width: 360, height: 800 });
  await page.goto('/conversations');
  const search = page.getByRole('searchbox');
  await expect(search).toHaveAccessibleName(
    appLanguage === 'ar' ? 'ابحث باسم العميل أو في الرسائل' : 'Search customer names or messages',
  );
  await search.fill('Opening message');
  await search.press('Enter');
  const row = page.locator('a.conversation-row').filter({ hasText: 'Reply number 49' });
  await expect(row).toHaveAttribute('href', new RegExp(e2eLongConversation()));
  await search.fill('طلب بلا نتيجة %_');
  await search.press('Enter');
  await expect(
    page.getByText(appLanguage === 'ar' ? 'لا توجد محادثات مطابقة' : 'No matching conversations'),
  ).toBeVisible();
  await page
    .getByRole('button', { name: appLanguage === 'ar' ? 'مسح البحث' : 'Clear search' })
    .click();
  await expect(search).toHaveValue('');
  await expect(search).toBeFocused();
  await expect(row).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(
    true,
  );
});
