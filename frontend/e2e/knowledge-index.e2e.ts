import type { Page } from '@playwright/test';
import { STEP_MS, closeWithin, expect, signedInTest as test, within } from './support/fixtures';

const WORDS = {
  en: { indexed: 'Indexed', needsIndex: 'Needs index' },
  ar: { indexed: 'مفهرس', needsIndex: 'يحتاج إلى فهرسة' },
};

/** The SignalR JSON protocol's record separator. */
const RECORD = '\u001e';

/**
 * Resolves once this tab's hub connection has joined the knowledge group: the
 * `SubscribeKnowledge` invocation went out and the server completed it (BA-11).
 * Without this the journey could index before the list is listening, and prove
 * nothing. Listens from the first socket the tab opens.
 */
function joinsKnowledge(tab: Page): Promise<void> {
  return new Promise((resolve) => {
    tab.on('websocket', (socket) => {
      const asked = new Set<string>();
      const messages = (payload: string | { toString(): string }) =>
        String(payload)
          .split(RECORD)
          .filter(Boolean)
          .flatMap((text) => {
            try {
              return [JSON.parse(text) as Record<string, unknown>];
            } catch {
              return [];
            }
          });

      socket.on('framesent', ({ payload }) => {
        for (const message of messages(payload)) {
          if (message['target'] === 'SubscribeKnowledge' && message['invocationId']) {
            asked.add(String(message['invocationId']));
          }
        }
      });
      socket.on('framereceived', ({ payload }) => {
        for (const message of messages(payload)) {
          if (message['type'] === 3 && asked.has(String(message['invocationId']))) {
            resolve();
          }
        }
      });
    });
  });
}

/**
 * Indexing ends by itself (Phase 38.6; extended in 38.8 for BA-10 and BA-11).
 *
 * A file is uploaded and indexed by the stand-in. The tab that pressed Index reloads
 * at once, which no longer stops the attempt (BA-10). Another tab has the knowledge
 * list open throughout: it hears the attempt settle over its one connection and
 * shows the document as indexed without being reloaded (BA-11).
 */
test('indexing ends by itself: a reload does not stop it, and an open list updates live', async ({
  page,
  appLanguage,
}) => {
  const words = WORDS[appLanguage];
  const name = `e2e-returns-${appLanguage}`;

  await page.goto('/knowledge/upload');

  // A real file through the real input: the page reads it, the server reads it again.
  // Named for the project, so this journey finds its own document whatever else is listed.
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

  // The list, in a second tab of the same member, listening before anything is indexed.
  const list = await within(page.context().newPage(), 'opening the list tab');

  try {
    await list.addInitScript((language: string) => {
      localStorage.setItem('omniai.lang', language);
    }, appLanguage);

    const joined = joinsKnowledge(list);

    await list.goto('/knowledge');

    const row = list.getByTestId('documents').locator('li', { hasText: name });

    await expect(row).toHaveCount(1);
    await expect(row.getByTestId('document-state')).toContainText(words.needsIndex);
    await within(joined, 'joining the knowledge group');

    // Index, and reload the tab that asked as soon as the request is out (BA-10).
    await Promise.all([
      page.waitForRequest(
        (request) =>
          request.method() === 'POST' && /\/knowledge\/[^/]+\/index$/.test(request.url()),
        { timeout: STEP_MS },
      ),
      page.getByTestId('index-button').click(),
    ]);
    await page.reload();

    // The open list ends "Needs index" or "Indexing now" by itself (BA-11).
    await expect(row.getByTestId('document-state')).toContainText(words.indexed, {
      timeout: STEP_MS,
    });

    // The reloaded editor says how it ended, and asks nobody to reload.
    await expect(page.getByTestId('readiness')).toContainText(words.indexed, {
      timeout: STEP_MS,
    });
    await expect(page.getByTestId('attempt-started').locator('time')).toBeVisible();
    await expect(row.getByTestId('document-size')).toContainText('53');
  } finally {
    await closeWithin(list);
  }
});
