import { post } from './support/api';
import { directionOf, e2eWorkspace, expect, signedInTest as test } from './support/fixtures';

test('a local Messenger customer receives AI and team replies in the same conversation', async ({
  page,
  appLanguage,
}, info) => {
  const arabic = appLanguage === 'ar';
  await page.emulateMedia({ colorScheme: arabic ? 'dark' : 'light' });
  await page.setViewportSize({ width: 1440, height: 900 });
  await page.goto('/channels');
  await page.locator('a[href="/channels/messenger-simulator"]').click();
  await expect(page.locator('html')).toHaveAttribute('dir', directionOf(appLanguage));
  const agents = page.locator('#sim-agent');
  await expect(agents.locator('option')).toHaveCount(2);
  const index = arabic ? 1 : 0;
  await agents.selectOption({ index });
  const agent = await agents.inputValue();
  const path =
    '/api/tenants/' + e2eWorkspace().tenantId + '/messenger-simulator/' + agent + '/page-events';
  const input = page.locator('#sim-text');
  const submit = page.locator('form button[type="submit"]');
  const first = arabic ? 'مرحباً، متى يصل طلبي؟' : 'Hello, when will my order arrive?';
  const second = arabic ? 'هل يمكن تغيير وقت التوصيل؟' : 'Can I change the delivery time?';
  const reply = arabic
    ? 'نعم، سأرتب معك موعداً جديداً.'
    : 'Yes, I can arrange another delivery time with you.';
  await input.fill(first);
  await agents.selectOption({ index: 1 - index });
  await expect(input).toHaveValue('');
  await agents.selectOption({ index });
  await expect(input).toHaveValue(first);
  const scenario = page.locator('#sim-scenario');
  await scenario.selectOption('LoseConfirmation');
  const [sent] = await Promise.all([
    page.waitForResponse(
      (response) =>
        new URL(response.url()).pathname === path && response.request().method() === 'POST',
    ),
    submit.click(),
  ]);
  expect(sent.status()).toBe(200);
  const mailbox = await sent.json();
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(1);
  const duplicate = await post(page.request, path, sent.request().postDataJSON());
  expect(await duplicate.json()).toEqual(mailbox);
  const echoed = sent.request().postDataJSON();
  const inbound = echoed.entry[0].messaging[0];
  // A pending request from a previous simulator version still uses the same stored key.
  const legacyPath = path.slice(0, -'/page-events'.length);
  const legacy = await post(page.request, legacyPath, {
    eventId: inbound.message.mid.replace(/^(.{8})(.{4})(.{4})(.{4})(.{12})$/, '$1-$2-$3-$4-$5'),
    text: first,
  });
  expect(await legacy.json()).toEqual(mailbox);
  echoed.entry[0].messaging = [
    {
      sender: inbound.recipient,
      recipient: inbound.sender,
      message: { mid: 'echo-1', text: first, is_echo: true },
    },
    { sender: inbound.sender, recipient: inbound.recipient, read: { watermark: Date.now() } },
  ];
  expect(await (await post(page.request, path, echoed)).json()).toEqual(mailbox);

  // The entire batch is checked before the first new customer turn is opened.
  const mixed = structuredClone(echoed);
  const newText = { ...inbound, message: { mid: 'm.batch/1+', text: 'Fixture message' } };
  mixed.entry[0].messaging = [newText, { ...newText, sender: { id: 'another-visitor' } }];
  await expect(post(page.request, path, mixed)).rejects.toThrow('answered 404');
  expect(await (await post(page.request, path, echoed)).json()).toEqual(mailbox);

  // The receiver already has the reply despite the lost confirmation. Reconciliation
  // clears the issue while preserving the same message ID and history.
  const uncertain = page.locator('[data-delivery="Unconfirmed"]');
  await expect(uncertain).toHaveCount(1);
  await uncertain.getByRole('button').click();
  await expect(uncertain).toHaveCount(0);
  expect((await (await post(page.request, legacyPath + '/receive', {})).json()).messages).toEqual(
    mailbox.messages,
  );
  await scenario.selectOption('Refuse');

  // The customer and the operator see the same conversation from different tabs.
  const inboxPath = await page.getByTestId('sim-inbox').getAttribute('href');
  expect(inboxPath).toBe('/conversations/' + mailbox.conversationId);
  const inbox = await page.context().newPage();
  await inbox.goto(inboxPath!);
  await inbox.getByTestId('take-over').click();
  await expect(inbox.getByTestId('return-to-ai')).toBeVisible();
  await input.fill(second);
  await submit.click();
  await expect(page.locator('[data-sender="Customer"]').last()).toContainText(second);
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(1);
  await expect(inbox.getByTestId('transcript')).toContainText(second);
  await inbox.locator('#conversation-reply').fill(reply);
  await inbox.getByTestId('send-reply').click();
  await expect(inbox.getByTestId('transcript')).toContainText(reply);
  await page.bringToFront();
  const refused = page.locator('[data-delivery="Refused"]');
  await expect(refused).toContainText(reply, { timeout: 12000 });
  await expect(inbox.locator('[data-sim-delivery="Refused"]')).toHaveCount(1, { timeout: 12000 });
  await expect(page.locator('[data-sender="HumanAgent"]')).toHaveCount(0);
  await page.reload();
  await agents.selectOption({ index });
  await expect(refused).toContainText(reply);
  await refused.getByRole('button').click();
  await expect(refused).toHaveCount(0);
  await expect(inbox.locator('[data-sim-delivery="Confirmed"]')).toHaveCount(2, { timeout: 12000 });
  await expect(page.locator('[data-sender="HumanAgent"]')).toHaveCount(1);
  await expect(page.locator('[data-sender="HumanAgent"]')).toContainText(reply);
  await page.reload();
  await agents.selectOption({ index });
  await expect(page.locator('[data-sender="HumanAgent"]')).toContainText(reply);
  await expect(page.locator('[data-sender]')).toHaveCount(4);
  for (const width of [1440, 390]) {
    await page.setViewportSize({ width, height: width === 390 ? 844 : 900 });
    await expect
      .poll(() => page.evaluate(() => document.documentElement.scrollWidth))
      .toBeLessThanOrEqual(width);
    if (width === 390) await submit.scrollIntoViewIfNeeded();
    await info.attach('messenger-' + width, {
      body: await page.screenshot({ fullPage: true }),
      contentType: 'image/png',
    });
  }

  const batch = structuredClone(echoed);
  batch.entry[0].messaging = [
    { ...inbound, message: { mid: 'm.opaque/1+', text: 'First batched text' } },
    { ...inbound, message: { mid: 'm.opaque/2+', text: 'Second batched text' } },
  ];
  const delivered = await (await post(page.request, path, batch)).json();
  expect(delivered.messages.slice(-2).map((m: { text: string }) => m.text)).toEqual([
    'First batched text',
    'Second batched text',
  ]);
  expect(await (await post(page.request, path, batch)).json()).toEqual(delivered);
});
