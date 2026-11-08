import { Page } from '@playwright/test';
import { expect, signedInTest as test } from './support/fixtures';

const NEEDS_PERSON = '/sounds/kalimba-needs-person.v1.wav';

/**
 * Records every file the page really starts playing, in `window.__e2eSounds`.
 *
 * The journey's own wrappers around the browser's audio calls, so the product
 * needs no hook of any kind for this to be observed.
 */
async function observeSounds(page: Page): Promise<void> {
  await page.addInitScript(() => {
    const files = new WeakMap<object, string>();
    const played: string[] = [];

    Object.defineProperty(window, '__e2eSounds', { value: played });

    const arrayBuffer = Response.prototype.arrayBuffer;

    Response.prototype.arrayBuffer = async function () {
      const data = await arrayBuffer.call(this);

      if (this.url.endsWith('.wav')) {
        files.set(data, new URL(this.url).pathname);
      }

      return data;
    };

    const decode = BaseAudioContext.prototype.decodeAudioData;

    BaseAudioContext.prototype.decodeAudioData = async function (data: ArrayBuffer) {
      const file = files.get(data);
      const buffer = await decode.call(this, data);

      if (file) {
        files.set(buffer, file);
      }

      return buffer;
    };

    const start = AudioBufferSourceNode.prototype.start;

    AudioBufferSourceNode.prototype.start = function (...args) {
      const file = this.buffer ? files.get(this.buffer) : undefined;

      if (file) {
        played.push(file);
      }

      return start.apply(this, args);
    };
  });
}

const playedSounds = (page: Page): Promise<string[]> =>
  page.evaluate(() => (window as unknown as { __e2eSounds: string[] }).__e2eSounds);

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
