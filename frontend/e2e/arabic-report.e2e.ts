import { readFileSync } from 'node:fs';
import { expect, signedInTest as test } from './support/fixtures';

/**
 * A report file in Arabic (Phase 38.5, E8): the form sends the language chosen,
 * the generated report carries a small tag saying it, and the file the button
 * saves is the Arabic one (a UTF-8 CSV with its byte-order mark and Arabic
 * headings), under the name the server gave it: since Phase 39.7a the page reads
 * the file through the client (note 16) instead of following a plain link.
 * Each project asks for one Arabic CSV of the run's own workspace.
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

  const [read, saved] = await Promise.all([
    page.waitForResponse((candidate) => new URL(candidate.url()).pathname.endsWith('/download')),
    page.waitForEvent('download'),
    row.getByTestId('download').click(),
  ]);

  expect(read.status()).toBe(200);

  // Saved under the server's own name, not one the page made up.
  const disposition = read.headers()['content-disposition'] ?? '';

  expect(saved.suggestedFilename()).toMatch(/\.csv$/);
  expect(disposition).toContain(saved.suggestedFilename());

  const text = readFileSync((await saved.path())!, 'utf8');

  expect(text.charCodeAt(0)).toBe(0xfeff);
  expect(text.split('\n')[0]).toMatch(/[؀-ۿ]/);
});
