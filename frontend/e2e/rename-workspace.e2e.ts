import { Page } from '@playwright/test';
import { e2eWorkspace, expect, signedInTest as test } from './support/fixtures';

/**
 * Renaming the journeys' own workspace (Phase 38.4, D1): the second tab still
 * holds the version it read, so its save is refused as stale and keeps what was
 * typed; the visitor's chat page shows the new name at once; and the name is put
 * back. Every name keeps the run's "E2E" prefix, so the workspace stays
 * recognisable for removal.
 */
test('a rename in one tab makes the other tab stale, and visitors see the new name', async ({
  page,
  context,
  appLanguage,
}) => {
  const { chatKey } = e2eWorkspace();

  /** Presses Save name on a tab and returns the status of the PUT it sent. */
  async function saveOn(tab: Page): Promise<number> {
    const [response] = await Promise.all([
      tab.waitForResponse(
        (candidate) => candidate.request().method() === 'PUT' && candidate.url().endsWith('/name'),
      ),
      tab.getByTestId('save-name').click(),
    ]);

    return response.status();
  }

  /** Another tab of the same browser, in the project's language. */
  async function anotherTab(): Promise<Page> {
    const tab = await context.newPage();

    await tab.addInitScript((language: string) => {
      localStorage.setItem('omniai.lang', language);
    }, appLanguage);

    return tab;
  }

  const stale = await anotherTab();

  await page.goto('/workspace-settings');
  await stale.goto('/workspace-settings');

  const field = page.getByTestId('workspace-name-input');
  const original = await field.inputValue();
  const renamed = `${original} ${appLanguage}`;

  expect(original).toMatch(/^E2E /);
  await expect(page.locator('h1')).toHaveCount(1);

  await field.fill(renamed);
  await expect(page.getByTestId('saved-name')).toContainText(original);
  expect(await saveOn(page)).toBe(200);

  await expect(page.locator('.toast-region[role="status"]')).toContainText(renamed);
  await expect(page.getByTestId('saved-name')).toHaveCount(0);

  // The second tab read the old version: its save is refused, its typing kept.
  const typed = `${original} stale`;

  await stale.getByTestId('workspace-name-input').fill(typed);
  expect(await saveOn(stale)).toBe(412);

  await expect(stale.getByTestId('settings-stale')).toBeVisible();
  await expect(stale.getByTestId('workspace-name-input')).toHaveValue(typed);

  // Reloading reads the saved name beside what was typed, and keeps the typing.
  await stale.getByTestId('settings-reload').click();
  await expect(stale.getByTestId('settings-stale')).toHaveCount(0);
  await expect(stale.getByTestId('saved-name')).toContainText(renamed);
  await expect(stale.getByTestId('workspace-name-input')).toHaveValue(typed);

  // Visitors see the new name on the chat page, at once.
  const visitor = await anotherTab();

  await visitor.goto(`/chat/${chatKey}`);
  await expect(visitor.getByTestId('chat-title')).toContainText(renamed);

  // Put the name back, from the tab that is current.
  await field.fill(original);
  expect(await saveOn(page)).toBe(200);
  await expect(page.getByTestId('saved-name')).toHaveCount(0);
  await expect(field).toHaveValue(original);

  await visitor.close();
  // The stale tab still holds unsaved typing; close it without the leave prompt.
  await stale.close({ runBeforeUnload: false });
});
