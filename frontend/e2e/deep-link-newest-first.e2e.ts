import { APIRequestContext, request as apiRequest } from '@playwright/test';
import { post } from './support/api';
import { e2eWorkspace, expect, signedInTest as test } from './support/fixtures';
import { BASE_URL } from './support/target';

/** More than one window, so the older ones have to be read. */
const REPLIES = 49;

/** A visitor writes in, and the AI's reply is stored, so the count below is settled. */
async function conversationAnsweredByAi(
  owner: APIRequestContext,
  tenantId: string,
  chatKey: string,
): Promise<string> {
  const visitor = await apiRequest.newContext({ baseURL: BASE_URL });

  try {
    const started = await post(visitor, `/api/public/webchat/${chatKey}/conversations`, {});
    const { conversationId } = await started.json();

    await post(visitor, `/api/public/webchat/conversations/${conversationId}/messages`, {
      clientMessageId: crypto.randomUUID(),
      text: 'Opening message',
    });

    await expect
      .poll(
        async () => {
          const detail = await owner.get(
            `/api/tenants/${tenantId}/conversations/${conversationId}`,
          );

          return (await detail.json()).messageCount;
        },
        { timeout: 20_000 },
      )
      .toBeGreaterThanOrEqual(2);

    return conversationId;
  } finally {
    await visitor.dispose();
  }
}

test('a deep link opens at the newest messages, and reads older ones going up', async ({
  page,
}) => {
  const { tenantId, chatKey } = e2eWorkspace();
  const conversationId = await conversationAnsweredByAi(page.request, tenantId, chatKey);
  const base = `/api/tenants/${tenantId}/conversations/${conversationId}`;
  const opened = await page.request.get(base);

  await post(page.request, `${base}/take-over`, {}, { 'If-Match': opened.headers()['etag'] });

  for (let reply = 1; reply <= REPLIES; reply++) {
    await post(page.request, `${base}/messages`, {
      clientMessageId: crypto.randomUUID(),
      text: `Reply number ${reply}`,
    });
  }

  const total = (await (await page.request.get(base)).json()).messageCount as number;
  const transcriptReads: string[] = [];

  page.on('request', (request) => {
    const url = new URL(request.url());

    if (url.pathname.endsWith(`/conversations/${conversationId}/messages`)) {
      transcriptReads.push(url.search);
    }
  });

  await page.goto(`/conversations/${conversationId}`);

  const bubbles = page.getByTestId('transcript').locator('.bubble');

  await expect(bubbles).toHaveCount(40);
  await expect(bubbles.last()).toContainText(`Reply number ${REPLIES}`);
  await expect(bubbles.last()).toBeInViewport();
  expect(transcriptReads).toEqual(['?tail=true&limit=40&include=events']);

  // The reader goes up: the page before arrives, and what they were reading stays put.
  const reading = bubbles.first();
  const readingText = await reading.locator('.message-text').innerText();

  await reading.scrollIntoViewIfNeeded();
  await expect(bubbles).toHaveCount(total);

  const stillThere = bubbles.filter({ hasText: readingText });

  await expect(stillThere).toBeInViewport();
  expect(transcriptReads).toHaveLength(2);
  expect(transcriptReads[1]).toMatch(/^\?before=\d+&limit=40&include=events$/);

  // The handoff is older than the first window, so it arrived with the older page.
  await expect(page.getByTestId('transcript-event-TakenOver')).toBeVisible();
  await expect(page.getByTestId('transcript-start')).toBeAttached();
  await expect(page.getByTestId('show-earlier')).toHaveCount(0);
});
