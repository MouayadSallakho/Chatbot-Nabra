import { Page } from '@playwright/test';

/**
 * Records every file the page really starts playing, in `window.__e2eSounds`.
 *
 * The journey's own wrappers around the browser's audio calls, so the product
 * needs no hook of any kind for this to be observed.
 */
export async function observeSounds(page: Page): Promise<void> {
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

export const playedSounds = (page: Page): Promise<string[]> =>
  page.evaluate(() => (window as unknown as { __e2eSounds: string[] }).__e2eSounds);
