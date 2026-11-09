import { expect, signedInTest as test } from './support/fixtures';
import { observeSounds, playedSounds } from './support/sounds';

const NEEDS_PERSON = '/sounds/kalimba-needs-person.v1.wav';

test('the test button plays its sound, and the files load only then', async ({ page }) => {
  const fetched: string[] = [];

  page.on('request', (request) => {
    if (new URL(request.url()).pathname.startsWith('/sounds/')) {
      fetched.push(new URL(request.url()).pathname);
    }
  });

  await observeSounds(page);
  await page.goto('/overview');

  await page.locator('app-profile-menu [aria-haspopup="menu"]').click();
  await expect(page.getByTestId('sounds-switch')).toHaveAttribute('aria-checked', 'true');
  await page.getByTestId('sound-settings').click();

  await expect(page).toHaveURL(/\/notifications\/preferences#in-this-browser$/);
  await expect(page.locator('#browser-heading')).toBeFocused();
  expect(fetched).toEqual([]);

  await page.getByTestId('sound-test').click();

  await expect.poll(() => playedSounds(page)).toEqual([NEEDS_PERSON]);
  await expect(page.getByTestId('sound-test-announcement')).not.toBeEmpty();
  expect(fetched).toEqual([NEEDS_PERSON]);
});

test('the sounds switch stays as it was left after a reload', async ({ page }) => {
  await page.goto('/overview');

  const menu = page.locator('app-profile-menu [aria-haspopup="menu"]');

  await menu.click();
  await page.getByTestId('sounds-switch').click();
  await page.reload();
  await menu.click();

  await expect(page.getByTestId('sounds-switch')).toHaveAttribute('aria-checked', 'false');
});
