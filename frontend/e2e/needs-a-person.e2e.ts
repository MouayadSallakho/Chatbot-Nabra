import { APIRequestContext, request as apiRequest } from '@playwright/test';
import { post } from './support/api';
import { e2eWorkspace, expect, signedInTest as test } from './support/fixtures';
import { observeSounds, playedSounds } from './support/sounds';
import { BASE_URL } from './support/target';

const AI_FAILED = '/sounds/kalimba-ai-failed.v1.wav';

/** The row's reason in the needs-a-person view (BA-7), as each language says it. */
const AI_COULD_NOT_REPLY = { en: 'The AI could not reply', ar: 'تعذّر على الذكاء الاصطناعي الرد' };

/** A visitor writes in, as a real one would. The stand-in fails on `[[fail]]`. */
async function say(
  visitor: APIRequestContext,
  conversationId: string,
  text: string,
): Promise<void> {
  await post(visitor, `/api/public/webchat/conversations/${conversationId}/messages`, {
    clientMessageId: crypto.randomUUID(),
    text,
  });
}

test('a visitor arrives live, a failed turn is heard, and the count goes up by one', async ({
  page,
  appLanguage,
}) => {
  const { chatKey } = e2eWorkspace();
  const visitor = await apiRequest.newContext({ baseURL: BASE_URL });

  try {
    await observeSounds(page);
    await page.goto('/conversations');
    await expect(page.locator('.live-state.is-live')).toBeVisible();

    // The browser allows audio only after a person's own gesture.
    await page.locator('h1').first().click();

    const count = page.getByTestId('needs-person-count');

    // The number is on screen once the baseline read is in, and nothing played for it.
    await expect(count).toBeVisible({ timeout: 20_000 });

    const before = Number(await count.innerText());

    expect(await playedSounds(page)).toEqual([]);

    const started = await post(visitor, `/api/public/webchat/${chatKey}/conversations`, {});
    const { conversationId } = await started.json();
    const row = page.getByTestId(`conversation-${conversationId}`);

    await say(visitor, conversationId, 'Hello from the journey');

    // At the top of the list, live, with a dot that has a name.
    await expect(row).toBeVisible();
    await expect(page.locator('a.conversation-row').first()).toHaveAttribute(
      'data-testid',
      `conversation-${conversationId}`,
    );
    await expect(row.getByTestId('row-unread')).toBeAttached();

    // The stand-in fails like an unavailable provider, which the visitor is told as 503.
    await expect(say(visitor, conversationId, '[[fail]] this one')).rejects.toThrow(/503$/);

    await expect(count).toHaveText(String(before + 1), { timeout: 30_000 });
    await expect.poll(() => playedSounds(page), { timeout: 30_000 }).toContain(AI_FAILED);

    // On its own list, and the failure is on the transcript.
    await page.getByTestId('view-needs-person').click();
    await expect(page).toHaveURL(/view=needs-person/);
    await expect(row).toBeVisible();

    // The row says why it is there, in words beside the badge's glyph (BA-7).
    await expect(row.getByTestId('row-reason')).toHaveText(AI_COULD_NOT_REPLY[appLanguage]);

    await row.click();
    await expect(page.getByTestId('transcript-event-AiTurnFailed')).toBeVisible();

    // Looked at: the dot goes, and the view it was opened from is still the one showing.
    await expect(row.getByTestId('row-unread')).toHaveCount(0, { timeout: 15_000 });
    await expect(page).toHaveURL(/view=needs-person/);
  } finally {
    await visitor.dispose();
  }
});
