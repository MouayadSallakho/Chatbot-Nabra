import { request as apiRequest } from '@playwright/test';
import { post } from './support/api';
import { e2eWorkspace, expect, signedInTest as test } from './support/fixtures';
import { BASE_URL } from './support/target';

/** A visitor writes in through the public chat, as a real one would. */
async function visitorWritesIn(chatKey: string): Promise<string> {
  const visitor = await apiRequest.newContext({ baseURL: BASE_URL });

  try {
    const started = await post(visitor, `/api/public/webchat/${chatKey}/conversations`, {});
    const { conversationId } = await started.json();

    await post(visitor, `/api/public/webchat/conversations/${conversationId}/messages`, {
      clientMessageId: crypto.randomUUID(),
      text: 'Hello from the journey',
    });

    return conversationId;
  } finally {
    await visitor.dispose();
  }
}

test('the bell and the inbox both update live, on one connection', async ({ page }) => {
  const { tenantId, chatKey } = e2eWorkspace();
  const negotiations: string[] = [];

  page.on('request', (request) => {
    const path = new URL(request.url()).pathname;

    if (path.startsWith('/hubs/') && path.endsWith('/negotiate')) {
      negotiations.push(path);
    }
  });

  await page.goto('/conversations');

  // The inbox says when its group has been joined; the bell's is joined before it.
  await expect(page.locator('.live-state.is-live')).toBeVisible();

  const conversationId = await visitorWritesIn(chatKey);

  await expect(page.getByTestId(`conversation-${conversationId}`)).toBeVisible();

  const badge = page.getByTestId('notification-badge');
  const before = (await badge.count()) > 0 ? await badge.innerText() : '';

  // A finished report is a notification for the person who asked for it.
  await post(page.request, `/api/tenants/${tenantId}/reports`, {
    reportType: 'ConversationActivity',
    format: 'Csv',
    range: '7d',
    clientRequestId: crypto.randomUUID(),
  });

  await expect(badge).toBeVisible({ timeout: 30_000 });
  await expect(badge).not.toHaveText(before, { timeout: 30_000 });

  expect(negotiations).toEqual(['/hubs/workspace-inbox/negotiate']);
});
