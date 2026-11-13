import {
  STEP_MS,
  e2eHandoverChatKey,
  expect,
  openVisitor,
  signedInTest as test,
} from './support/fixtures';

/**
 * `[[slow]]` makes the stand-in wait ten seconds more before it answers (37.2).
 * The message is mostly in the project's language, so the reply is too.
 */
const WORDS = {
  en: {
    text: 'Is my order on its way yet?',
    reply: 'Local test reply:',
  },
  ar: {
    text: 'هل أصبح طلبي في الطريق إلينا الآن؟',
    reply: 'رد تجريبي محلي:',
  },
};

/** The stand-in's own wait for a reply marked `[[slow]]`: ten seconds on top of its one. */
const SLOW_REPLY_MS = 10_000;

/** How long the reply may take to reach both sides once it is due. */
const REPLY_MS = 25_000;

/**
 * A slow reply, seen live (Phase 38.6; rewritten in 38.8 for BA-13).
 *
 * The inbox is open before the visitor writes. Since 38.7a the server announces
 * the customer's message as soon as it is stored, so before the AI answers:
 * the new conversation appears on the open list with that message, and opened, it
 * says the AI has not replied yet. Only then does the reply arrive, on both sides.
 * Nothing here asks the API whether the message is stored: the open inbox is told.
 *
 * Every wait has a bound well inside the journey's minute (`STEP_MS` and the limits
 * below), and the visitor's browser is closed within one too, so a failure ends as
 * itself instead of holding the run.
 */
test('a slow reply: the open inbox shows the message and the wait live, then the reply', async ({
  page,
  browser,
  appLanguage,
}) => {
  const words = WORDS[appLanguage];

  // The inbox first, live, before anybody writes.
  await page.goto('/conversations');
  await expect(page.locator('.live-state.is-live')).toBeVisible({ timeout: STEP_MS });

  const { visitor, close } = await openVisitor(browser, e2eHandoverChatKey(), appLanguage);

  try {
    await visitor.getByTestId('chat-input').fill(`${words.text} [[slow]]`);

    const [started] = await Promise.all([
      visitor.waitForResponse(
        (response) =>
          response.request().method() === 'POST' &&
          /^\/api\/public\/webchat\/[^/]+\/conversations$/.test(new URL(response.url()).pathname),
        { timeout: STEP_MS },
      ),
      visitor.getByTestId('chat-send').click(),
    ]);
    const sentAt = Date.now();

    // A refused start (the public chat's limit, say) fails here, by name, at once.
    expect(started.ok(), 'the visitor could start a conversation').toBe(true);

    const { conversationId } = (await started.json()) as { conversationId: string };
    const typing = visitor.getByTestId('chat-typing');
    const visitorReply = visitor.locator('[data-sender="AiAgent"]');

    await expect(typing).toBeVisible({ timeout: STEP_MS });

    // The new conversation arrives on the open list with the customer's message,
    // from the "stored" announcement alone: the page is never reloaded.
    const row = page.getByTestId(`conversation-${conversationId}`);

    await expect(row).toBeVisible({ timeout: SLOW_REPLY_MS });
    await expect(row).toContainText(words.text);

    // Opened, it says a reply is still owed.
    await row.click();

    const awaiting = page.getByTestId('awaiting-ai');

    await expect(awaiting).toBeVisible({ timeout: SLOW_REPLY_MS });

    // All of that before the reply: nothing answered on either side yet, and inside
    // the ten seconds the stand-in holds the reply back.
    await expect(visitorReply).toHaveCount(0);
    await expect(typing).toBeVisible();
    expect(Date.now() - sentAt).toBeLessThan(SLOW_REPLY_MS);

    // The reply ends both waits, on both sides: the inbox hears of it live.
    await expect(visitorReply).toContainText(words.reply, { timeout: REPLY_MS });
    expect(Date.now() - sentAt).toBeGreaterThanOrEqual(SLOW_REPLY_MS);
    await expect(typing).toHaveCount(0, { timeout: STEP_MS });

    await expect(awaiting).toHaveCount(0, { timeout: STEP_MS });
    await expect(page.getByTestId('transcript').locator('.bubble').last()).toContainText(
      words.reply,
      { timeout: STEP_MS },
    );

    // The visitor saw their own message once, at the end as all along (38.8, item 2).
    await expect(visitor.locator('[data-sender="Customer"]')).toHaveCount(1);
  } finally {
    await close();
  }
});
