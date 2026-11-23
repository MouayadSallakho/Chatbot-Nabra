import { expect, signedInTest as test } from './support/fixtures';

test('shows first reply times on the overview and for the selected analytics period', async ({
  page,
  appLanguage,
}) => {
  await page.setViewportSize({ width: 360, height: 800 });
  const card = page.getByTestId('first-reply');
  const heading = appLanguage === 'ar' ? 'زمن أول رد' : 'Time to first reply';
  for (const [path, days] of [
    ['/', 7],
    ['/analytics?period=90d', 90],
  ] as const) {
    const response = page.waitForResponse(
      (res) =>
        res.url().includes('/analytics/dashboard?') &&
        new URL(res.url()).searchParams.get('range') === days + 'd',
    );
    await page.goto(path);
    const data = (await (await response).json()).summary.firstReply;
    expect(data.aiConversations).toBeGreaterThan(0);
    await expect(card.getByRole('heading', { name: heading })).toBeVisible();
    await expect(card.locator('p')).toContainText(String(days));
    await expect(card.locator('tbody tr').first().locator('td').last()).toHaveText(
      String(data.aiConversations),
    );
    await expect(card.locator('tbody tr').last().locator('td').last()).toHaveText(
      String(data.teamConversations),
    );
    expect(await card.evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
    if (path === '/') {
      await page.setViewportSize({ width: 1280, height: 900 });
      await expect(card).toBeVisible();
      expect(await card.evaluate((el) => el.scrollWidth <= el.clientWidth + 1)).toBe(true);
      await page.setViewportSize({ width: 360, height: 800 });
    }
  }
});
