import { expect, signedInTest as test } from './support/fixtures';

const MESSAGES = {
  en: { question: 'What are your opening hours?', reply: 'Local test reply:' },
  ar: { question: 'ما هي ساعات العمل لديكم؟', reply: 'رد تجريبي محلي:' },
};

test('the agent preview answers through the stand-in, and the agent says where it answers', async ({
  page,
  appLanguage,
}) => {
  const words = MESSAGES[appLanguage];
  const channelLists: string[] = [];

  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;

    if (/\/(website-chat|whatsapp\/connections)$/.test(path)) {
      channelLists.push(path);
    }
  });

  await page.goto('/ai-agents');
  await page.getByTestId('agents').locator('a').first().click();

  await expect(page.getByTestId('agent-channels').locator('a')).toHaveCount(1);

  const preview = page.getByTestId('agent-preview');

  await preview.getByTestId('chat-input').fill(words.question);
  await preview.getByTestId('chat-send').click();

  await expect(preview.locator('[data-sender="AiAgent"]')).toContainText(words.reply);

  // The agent reads carry its channels: neither channel list is asked for.
  expect(channelLists).toEqual([]);
});
