import { post } from './support/api';
import { directionOf, e2eWorkspace, expect, signedInTest as test } from './support/fixtures';

test('Instagram DMs survive replay and human takeover in their own inbox', async ({
  page,
  appLanguage,
}, info) => {
  const ar = appLanguage === 'ar';
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/channels');
  await page.locator('a[href="/channels/instagram-simulator"]').click();
  await expect(page.locator('html')).toHaveAttribute('dir', directionOf(appLanguage));
  const agent = page.locator('#sim-agent');
  await expect(agent.locator('option')).toHaveCount(2);
  const index = ar ? 1 : 0;
  await agent.selectOption({ index });
  const id = await agent.inputValue();
  const base = '/api/tenants/' + e2eWorkspace().tenantId;
  const path = base + '/instagram-simulator/' + id + '/direct-events';
  const text = page.locator('#sim-text');
  const submit = page.locator('form button[type="submit"]');
  await text.fill(ar ? 'مرحباً، هل الحقيبة متوفرة؟' : 'Hello, is the bag available?');
  const [result] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === path),
    submit.click(),
  ]);
  expect(result.status()).toBe(200);
  const stored = await result.json();
  const event = result.request().postDataJSON();
  expect(event.object).toBe('instagram');
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(1);
  expect(await (await post(page.request, path, event)).json()).toEqual(stored);
  await expect(post(page.request, path, { ...event, object: 'page' })).rejects.toThrow(
    'answered 400',
  );

  const mixed = structuredClone(event);
  const original = mixed.entry[0].messaging[0];
  original.message.mid = 'ig.opaque/01+';
  mixed.entry[0].messaging.push({ ...original, sender: { id: 'other-instagram-customer' } });
  await expect(post(page.request, path, mixed)).rejects.toThrow('answered 404');
  const ignored = structuredClone(event);
  ignored.entry[0].messaging = [
    { ...original, message: { ...original.message, is_echo: true } },
    { ...original, message: { ...original.message, is_deleted: true } },
    { sender: original.sender, recipient: original.recipient, read: { mid: 'read-only' } },
  ];
  expect(await (await post(page.request, path, ignored)).json()).toEqual(stored);
  const messengerBox = await (
    await post(page.request, base + '/messenger-simulator/' + id + '/receive', {})
  ).json();
  expect(messengerBox.conversationId).not.toBe(stored.conversationId);

  const inbox = await page.context().newPage();
  await inbox.goto((await page.getByTestId('sim-inbox').getAttribute('href')) as string);
  const toggle = inbox.getByTestId('toggle-details');
  await expect(toggle).toBeVisible();
  if ((await toggle.getAttribute('aria-expanded')) === 'false') await toggle.click();
  const agentName = await agent.locator('option:checked').innerText();
  await expect(inbox.locator('app-conversation-context')).toContainText(agentName);
  await inbox.getByTestId('take-over').click();
  await expect(inbox.getByTestId('return-to-ai')).toBeVisible();
  const second = ar ? 'أود التحدث مع فريق المتجر.' : 'I would like to speak with the shop team.';
  await text.fill(second);
  await submit.click();
  await expect(inbox.getByTestId('transcript')).toContainText(second);
  const reply = ar ? 'أهلاً بك، الحقيبة متوفرة باللون الأزرق.' : 'Hello, we have the bag in blue.';
  await inbox.locator('#conversation-reply').fill(reply);
  await inbox.getByTestId('send-reply').click();
  await page.bringToFront();
  await expect(page.locator('[data-sender="HumanAgent"]')).toHaveText(new RegExp(reply), {
    timeout: 12000,
  });
  await page.reload();
  await agent.selectOption({ index });
  await expect(page.locator('[data-sender]')).toHaveCount(4);
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(1);
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await expect
      .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
      .toBeLessThanOrEqual(width);
    await info.attach('instagram-' + width, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  }
});
