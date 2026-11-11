import { expect, signedInTest as test } from './support/fixtures';

/**
 * A report file in Arabic (Phase 38.5, E8): the form sends the language chosen,
 * the generated report carries a small tag saying it, and the file the link
 * downloads is the Arabic one (a UTF-8 CSV with its byte-order mark and Arabic
 * headings). Each project asks for one Arabic CSV of the run's own workspace.
 */
test('an Arabic report is generated, tagged, and downloads in Arabic', async ({ page }) => {
  await page.goto('/reports');
  await expect(page.locator('h1')).toHaveCount(1);

  // The segmented control's radios are visually hidden: a reader clicks the label.
  await page
    .locator('label.segmented-option', { has: page.getByTestId('report-language-ar') })
    .click();
  await expect(page.getByTestId('report-language-ar')).toBeChecked();

  const [posted] = await Promise.all([
    page.waitForResponse(
      (candidate) =>
        candidate.request().method() === 'POST' &&
        new URL(candidate.url()).pathname.endsWith('/reports'),
    ),
    page.getByTestId('generate').click(),
  ]);

  expect(posted.status()).toBe(202);
  expect(posted.request().postDataJSON()).toEqual(
    expect.objectContaining({ language: 'ar', format: 'Csv' }),
  );

  const execution = (await posted.json()) as { id: string; language: string };

  expect(execution.language).toBe('ar');

  // The page checks the report it just asked for until it settles.
  const row = page.getByTestId(`execution-${execution.id}`);

  await expect(row.getByTestId('language-ar')).toBeVisible();
  await expect(row.getByTestId('download')).toBeVisible({ timeout: 45_000 });
  await expect(row.getByTestId('download')).toHaveAttribute('aria-label', /Arabic file|ملف عربي/);

  const href = await row.getByTestId('download').getAttribute('href');
  const file = await page.request.get(href!);

  expect(file.status()).toBe(200);

  const text = (await file.body()).toString('utf8');

  expect(text.charCodeAt(0)).toBe(0xfeff);
  expect(text.split('\n')[0]).toMatch(/[؀-ۿ]/);
});
