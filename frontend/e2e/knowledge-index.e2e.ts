import { expect, signedInTest as test } from './support/fixtures';

const WORDS = {
  en: { indexed: 'Indexed', needsIndex: 'Needs index' },
  ar: { indexed: 'مفهرس', needsIndex: 'يحتاج إلى فهرسة' },
};

test('a file is uploaded, indexed by the stand-in, and the outcome survives a reload', async ({
  page,
  appLanguage,
}) => {
  const words = WORDS[appLanguage];

  await page.goto('/knowledge/upload');

  // A real file through the real input: the page reads it, the server reads it again.
  // Named for the project, so this journey finds its own document whatever else is listed.
  const name = `e2e-returns-${appLanguage}`;

  await page.locator('input[type=file]').evaluate((input: HTMLInputElement, fileName: string) => {
    const files = new DataTransfer();

    files.items.add(
      new File(['Goods may be returned within thirty days of delivery.'], `${fileName}.txt`, {
        type: 'text/plain',
      }),
    );
    input.files = files.files;
    input.dispatchEvent(new Event('change', { bubbles: true }));
  }, name);
  await page.getByTestId('upload-button').click();

  await expect(page.getByTestId('readiness')).toContainText(words.needsIndex);
  await page.getByTestId('index-button').click();
  await expect(page.getByTestId('index-result')).toBeVisible();

  await page.reload();
  await expect(page.getByTestId('readiness')).toContainText(words.indexed);
  await expect(page.getByTestId('attempt-started').locator('time')).toBeVisible();

  await page.goto('/knowledge');

  const row = page.getByTestId('documents').locator('li', { hasText: name });

  await expect(row).toHaveCount(1);

  await expect(row.getByTestId('document-state')).toContainText(words.indexed);
  await expect(row.getByTestId('document-size')).toContainText('53');
});
