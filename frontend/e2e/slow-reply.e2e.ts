import {
  e2eHandoverChatKey,
  e2eWorkspace,
  expect,
  openVisitor,
  signedInTest as test,
} from './support/fixtures';

/**
 * `[[slow]]` makes the stand-in wait ten seconds more before it answers (37.2).
 * The message is mostly in the project's language, so the reply is too.
 */
const WORDS = {
  en: { message: 'Is my order on its way yet? [[slow]]', reply: 'Local test reply:' },
  ar: { message: 'هل أصبح طلبي في الطريق إلينا الآن؟ [[slow]]', reply: 'رد تجريبي محلي:' },
};

/** The stand-in's own wait for a reply marked `[[slow]]`: ten seconds on top of its one. */
const SLOW_REPLY_MS = 10_000;

/**
 * A slow reply (Phase 38.6): while the AI is still answering, the visitor sees the
 * agent typing and the inbox says a reply is owed, both at once and both until the
 * reply arrives, which then shows on both sides.
 */
test('a slow reply keeps the visitor and the inbox waiting until it arrives', async ({
  page,
  browser,
  appLanguage,
}) => {
  const words = WORDS[appLanguage];
  const { visitor, close } = await openVisitor(browser, e2eHandoverChatKey(), appLanguage);

  try {
    await visitor.getByTestId('chat-input').fill(words.message);

    const [started] = await Promise.all([
      visitor.waitForResponse(
        (response) =>
          response.request().method() === 'POST' &&
          /^\/api\/public\/webchat\/[^/]+\/conversations$/.test(new URL(response.url()).pathname),
      ),
      visitor.getByTestId('chat-send').click(),
    ]);
    const sentAt = Date.now();
    const { conversationId } = (await started.json()) as { conversationId: string };

    const typing = visitor.getByTestId('chat-typing');
    const visitorReply = visitor.locator('[data-sender="AiAgent"]');

    await expect(typing).toBeVisible();

    // The server stores what the visitor said before it asks the AI, but tells an
    // open inbox only once the turn settles (a backend ask, 38.6). So the inbox is
    // opened once the message is stored, as a person would find it meanwhile.
    const { tenantId } = e2eWorkspace();
    const conversation = `/api/tenants/${tenantId}/conversations/${conversationId}`;
    const stored = async (): Promise<number> => {
      const read = await page.request.get(conversation);

      return ((await read.json()) as { messageCount: number }).messageCount;
    };

    await expect.poll(stored).toBe(1);
    await page.goto(`/conversations/${conversationId}`);
    await expect(page.locator('.live-state.is-live')).toBeVisible();

    // Opened, it says a reply is still owed.
    const awaiting = page.getByTestId('awaiting-ai');

    await expect(awaiting).toBeVisible();

    // Both sides are waiting at the same moment, and nothing has been answered yet.
    await expect(typing).toBeVisible();
    await expect(visitorReply).toHaveCount(0);

    // The reply ends both waits, on both sides: the inbox hears of it live.
    await expect(visitorReply).toContainText(words.reply, { timeout: 30_000 });
    expect(Date.now() - sentAt).toBeGreaterThanOrEqual(SLOW_REPLY_MS);
    await expect(typing).toHaveCount(0);

    await expect(awaiting).toHaveCount(0, { timeout: 15_000 });
    await expect(page.getByTestId('transcript').locator('.bubble').last()).toContainText(
      words.reply,
    );
  } finally {
    await close();
  }
});
