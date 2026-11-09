import {
  e2eLongConversation,
  e2eWorkspace,
  expect,
  signedInTest as test,
} from './support/fixtures';

/** How the run's long conversation ends: global setup wrote these. */
const LAST_REPLY = 'Reply number 49';

test('a deep link opens at the newest messages, and reads older ones going up', async ({
  page,
}) => {
  const { tenantId } = e2eWorkspace();
  const conversationId = e2eLongConversation();
  const base = `/api/tenants/${tenantId}/conversations/${conversationId}`;
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
  await expect(bubbles.last()).toContainText(LAST_REPLY);
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
