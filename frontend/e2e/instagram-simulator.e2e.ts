import { post } from './support/api';
import { directionOf, e2eWorkspace, expect, signedInTest as test } from './support/fixtures';

test('Instagram DMs survive replay and human takeover in their own inbox', async ({
  page,
  appLanguage,
}, info) => {
  test.setTimeout(100_000);
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
  const scenario = page.locator('#sim-scenario');
  await scenario.selectOption('Refuse');
  await text.fill(ar ? 'مرحباً، هل الحقيبة متوفرة؟' : 'Hello, is the bag available?');
  const [result] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === path),
    submit.click(),
  ]);
  expect(result.status()).toBe(200);
  const stored = await result.json();
  const event = result.request().postDataJSON();
  expect(event.object).toBe('instagram');
  const refused = page.locator('[data-delivery="Refused"]');
  await expect(refused).toHaveCount(1);
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(0);
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
  await refused.getByRole('button').click();
  await expect(refused).toHaveCount(0);
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(1);
  await scenario.selectOption('LoseConfirmation');
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
  const uncertain = page.locator('[data-delivery="Unconfirmed"]');
  await expect(uncertain).toHaveCount(1);
  await expect(inbox.locator('[data-sim-delivery="Unconfirmed"]')).toHaveCount(1, {
    timeout: 12000,
  });
  await page.reload();
  await agent.selectOption({ index });
  await expect(uncertain).toHaveCount(1);
  const receiptPath = base + '/instagram-simulator/' + id + '/mailbox';
  const before = (await (await page.request.get(receiptPath)).json()).messages;
  await uncertain.getByRole('button').click();
  await expect(uncertain).toHaveCount(0);
  expect((await (await page.request.get(receiptPath)).json()).messages).toEqual(before);
  await expect(inbox.locator('[data-sim-delivery="Confirmed"]')).toHaveCount(2, { timeout: 12000 });
  await inbox.getByTestId('return-to-ai').click();
  await expect(inbox.getByTestId('take-over')).toBeVisible();

  // External control is independent of Nabra's AI/staff switch.
  const switchOwner = page.getByTestId('ig-switch-owner');
  const notifyPath = base + '/instagram-simulator/' + id + '/handover-events';
  const [taken] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === notifyPath),
    switchOwner.click(),
  ]);
  const notice = taken.request().postDataJSON();
  const initial = await taken.json();
  expect(initial.outcome).toBe('Applied');
  const [duplicate] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === notifyPath),
    page.getByTestId('ig-replay-takeover').click(),
  ]);
  expect(duplicate.request().postDataJSON()).toEqual(notice);
  const repeated = await duplicate.json();
  expect(repeated.outcome).toBe('Duplicate');
  expect(repeated.mailbox).toEqual(initial.mailbox);
  await expect(page.locator('[data-handover="Duplicate"]')).toBeVisible();
  // A previously unseen older return must not re-enable replies.
  const delayedReturn = structuredClone(notice);
  const delayed = delayedReturn.entry[0].changes[0].value;
  delayed.timestamp -= 100;
  delayed.pass_thread_control = { previous_owner_app_id: '10002', new_owner_app_id: '10001' };
  delete delayed.take_thread_control;
  const discarded = await (await post(page.request, notifyPath, delayedReturn)).json();
  expect(discarded.outcome).toBe('Stale');
  expect(discarded.mailbox.ownership.externalOwner).toBe(true);
  const other = structuredClone(notice);
  other.entry[0].changes[0].value.sender.id = 'another-visitor';
  await expect(post(page.request, notifyPath, other)).rejects.toThrow('answered 404');
  await expect(inbox.getByTestId('ig-inbox-paused')).toBeVisible({ timeout: 12000 });
  await page.reload();
  await agent.selectOption({ index });
  const waiting = ar ? 'وصل سؤالي أثناء التحويل.' : 'My question during the transfer.';
  await page.locator('#sim-media').selectOption('upload');
  await page.locator('#sim-image').setInputFiles('public/media/parcel-label.png');
  await text.fill(waiting);
  const [paused] = await Promise.all([
    page.waitForResponse((r) => new URL(r.url()).pathname === path),
    submit.click(),
  ]);
  const pausedBox = await paused.json();
  expect(pausedBox.ownership.externalOwner).toBe(true);
  expect(pausedBox.messages).toHaveLength(5);
  expect(pausedBox.messages.at(-1).attachments).toEqual([
    expect.stringMatching(/^data:image\/jpeg;base64,/u),
  ]);
  const receipt = await (await post(page.request, path, paused.request().postDataJSON())).json();
  expect(receipt.messages).toEqual(pausedBox.messages);
  await expect(inbox.getByTestId('transcript')).toContainText(waiting);
  const preview = inbox.getByTestId('transcript').locator('app-message-attachments img');
  await expect(preview).toHaveCount(1);
  await expect.poll(() => preview.evaluate((img: HTMLImageElement) => img.naturalWidth)).toBe(320);
  await inbox.getByTestId('take-over').click();
  await expect(inbox.getByTestId('return-to-ai')).toBeVisible();
  await expect(inbox.locator('#conversation-reply')).toHaveCount(0);
  await expect(
    post(page.request, base + '/conversations/' + stored.conversationId + '/messages', {
      clientMessageId: crypto.randomUUID(),
      text: 'Busy',
    }),
  ).rejects.toThrow('answered 409');
  await switchOwner.click();
  await expect(inbox.locator('#conversation-reply')).toBeVisible({ timeout: 12000 });
  const late = await (await post(page.request, notifyPath, notice)).json();
  expect(late.outcome).toBe('Stale');
  expect(late.mailbox.ownership).toEqual({
    externalOwner: false,
    version: initial.mailbox.ownership.version + 1,
  });
  await inbox.getByTestId('return-to-ai').click();
  await expect(inbox.getByTestId('take-over')).toBeVisible();
  expect(
    (await (await post(page.request, path, paused.request().postDataJSON())).json()).messages,
  ).toEqual(pausedBox.messages);
  await text.fill(ar ? 'هل يمكن متابعة طلبي الآن؟' : 'Can we continue my order now?');
  await submit.click();
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(2);
  await page.reload();
  await agent.selectOption({ index });
  await expect(page.locator('[data-sender]')).toHaveCount(7);
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(2);
  for (const [option, label] of [
    ['unavailable', ar ? 'تعذّرت معاينة الصورة.' : 'The image preview is unavailable.'],
    [
      'unsupported',
      ar ? 'لا تتوفر معاينة لهذا النوع من المرفقات.' : 'This attachment type has no preview.',
    ],
  ]) {
    await page.locator('#sim-media').selectOption(option);
    await submit.click();
    await expect(inbox.getByTestId('transcript')).toContainText(label);
    await expect(
      inbox.locator(
        `a.conversation-row[href="/conversations/${stored.conversationId}"] .row-preview`,
      ),
    ).toContainText(ar ? 'مرفق' : 'Attachment');
    await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(2);
  }
  await inbox.reload();
  await expect(preview).toHaveCount(1);
  await expect(inbox.getByTestId('transcript').locator('app-message-attachments')).toHaveCount(3);
  await text.fill(ar ? 'متى يصل الطرد؟' : 'When will the parcel arrive?');
  await submit.click();
  await expect(page.locator('[data-sender="AiAgent"]')).toHaveCount(3);
  await info.attach('instagram-staff-attachments', {
    body: await inbox.screenshot(),
    contentType: 'image/png',
  });
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
