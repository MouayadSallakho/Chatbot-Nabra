import { e2eHandoverChatKey, expect, openVisitor, signedInTest as test } from './support/fixtures';

const WORDS = {
  en: {
    first: 'Can I change the delivery address?',
    person: 'Yes, I have changed it for you.',
    again: 'Thank you, and when will it arrive?',
    reply: 'Local test reply:',
  },
  ar: {
    first: 'هل يمكنني تغيير عنوان التوصيل؟',
    person: 'نعم، غيّرته لك.',
    again: 'شكرًا لك، ومتى يصل الطلب؟',
    reply: 'رد تجريبي محلي:',
  },
};

/**
 * A person steps in and steps out again (Phase 38.6): taking over puts the
 * composer in front of them and the divider on the transcript; the visitor reads
 * the person's reply live; returning to the AI draws the second divider, and the
 * visitor's next message is answered by the AI again.
 */
test('a person takes over and answers, then returns the conversation to the AI', async ({
  page,
  browser,
  appLanguage,
}) => {
  const words = WORDS[appLanguage];
  const { visitor, close } = await openVisitor(browser, e2eHandoverChatKey(), appLanguage);

  try {
    const fromAi = visitor.locator('[data-sender="AiAgent"]');
    const dividers = visitor.getByTestId('chat-handoff-divider');

    await visitor.getByTestId('chat-input').fill(words.first);

    const [started] = await Promise.all([
      visitor.waitForResponse(
        (response) =>
          response.request().method() === 'POST' &&
          /^\/api\/public\/webchat\/[^/]+\/conversations$/.test(new URL(response.url()).pathname),
      ),
      visitor.getByTestId('chat-send').click(),
    ]);
    const { conversationId } = (await started.json()) as { conversationId: string };

    await expect(fromAi).toHaveCount(1);
    await expect(fromAi).toContainText(words.reply);

    // Taking over: the button becomes Return to AI, and the composer is next.
    await page.goto(`/conversations/${conversationId}`);
    await page.getByTestId('take-over').click();

    await expect(page.getByTestId('return-to-ai')).toBeVisible();
    await expect(page.locator('#conversation-reply')).toBeFocused();
    await expect(page.getByTestId('transcript-event-TakenOver')).toBeVisible();

    // The person answers; the visitor reads it live, after the line saying a person joined.
    await page.locator('#conversation-reply').fill(words.person);
    await page.getByTestId('send-reply').click();

    await expect(visitor.locator('[data-sender="HumanAgent"]')).toContainText(words.person);
    await expect(dividers).toHaveCount(1);

    // Back to the AI: the button and the focus return, and the second divider is drawn.
    await page.getByTestId('return-to-ai').click();

    await expect(page.getByTestId('take-over')).toBeFocused();
    await expect(page.getByTestId('transcript-event-ReturnedToAi')).toBeVisible();

    // The visitor's next message is the AI's to answer again, and the inbox shows it live.
    await visitor.getByTestId('chat-input').fill(words.again);
    await visitor.getByTestId('chat-send').click();

    await expect(fromAi).toHaveCount(2);
    await expect(fromAi.last()).toContainText(words.reply);
    await expect(dividers).toHaveCount(2);
    await expect(page.getByTestId('transcript').locator('.bubble').last()).toContainText(
      words.reply,
    );
  } finally {
    await close();
  }
});
