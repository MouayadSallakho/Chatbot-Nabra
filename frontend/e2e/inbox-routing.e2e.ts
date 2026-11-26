import { randomBytes } from 'node:crypto';
import { post } from './support/api';
import { expect, test } from './support/fixtures';

test('new customer messages route by channel and language with an unassigned fallback', async ({
  page,
  appLanguage,
}, info) => {
  test.setTimeout(100_000);
  const id = randomBytes(5).toString('hex');
  const email = 'routing.' + id + '@example.test';
  await post(page.request, '/api/auth/register', {
    email,
    password: randomBytes(24).toString('base64url'),
    language: appLanguage,
  });
  const tenant = await (
    await post(page.request, '/api/tenants', { name: 'Nabra routing', slug: 'route-' + id })
  ).json();
  const base = '/api/tenants/' + tenant.id;
  const agent = await post(page.request, base + '/ai-agents', {
    name: 'Support',
    instructions: 'Answer briefly.',
  });
  await post(
    page.request,
    base + '/ai-agents/' + (await agent.json()).id + '/activate',
    {},
    { 'If-Match': agent.headers()['etag'] },
  );
  const empty = await (
    await post(page.request, base + '/roles', {
      name: 'Support overflow',
      permissions: ['conversations.view', 'conversations.reply'],
    })
  ).json();
  await page.goto('/workspace-settings');
  await page.getByTestId('routing-settings').click();
  const matched = page.locator('[data-route="Messenger-ar"]');
  const fallback = page.locator('[data-route="Instagram-en"]');
  await matched.selectOption({ label: 'Owner' });
  await expect(matched).toBeEnabled();
  await fallback.selectOption({ label: 'Support overflow' });
  await expect(fallback).toBeEnabled();
  await page.reload();
  await page.getByTestId('routing-settings').click();
  await expect(matched.locator('option:checked')).toHaveText('Owner');
  await expect(fallback).toHaveValue(empty.id);
  await info.attach('routing-settings', {
    body: await page.screenshot(),
    contentType: 'image/png',
  });

  async function send(channel: string, message: string, agentName?: string) {
    await page.goto('/channels/' + channel + '-simulator');
    if (agentName) await page.locator('#sim-agent').selectOption({ label: agentName });
    await page.locator('#sim-text').fill(message);
    const [response] = await Promise.all([
      page.waitForResponse((r) => /\/(page|direct)-events(?:\?|$)/u.test(r.url())),
      page.locator('form button[type="submit"]').click(),
    ]);
    expect(response.status()).toBe(200);
    return (await response.json()).conversationId as string;
  }
  const assignedId = await send('messenger', 'مرحباً، متى يصل طلبي؟');
  const generalId = await send('instagram', 'Hello, when will my order arrive?');
  async function detail(cid: string) {
    const response = await page.request.get(base + '/conversations/' + cid);
    expect(response.ok()).toBe(true);
    return response.json();
  }
  expect((await detail(assignedId)).assignedTo.email).toBe(email);
  expect((await detail(generalId)).assignedTo).toBeNull();
  await page.goto('/conversations/' + assignedId);
  await expect(
    page.locator('a.conversation-row').filter({ has: page.getByTestId('row-assigned') }),
  ).toHaveCount(1);
  await info.attach('assigned-inbox', { body: await page.screenshot(), contentType: 'image/png' });
  const unassigned = await (await page.request.get(base + '/conversations?unassigned=true')).json();
  expect(unassigned.items.map((row: { id: string }) => row.id)).toEqual([generalId]);
  // Changes affect new conversations. A second language in an existing thread must not reroute it.
  await page.goto('/workspace-settings');
  await page.getByTestId('routing-settings').click();
  await matched.selectOption('');
  await expect(matched).toBeEnabled();
  await fallback.selectOption({ label: 'Owner' });
  await expect(fallback).toBeEnabled();
  expect(await send('instagram', 'Please check the order again.')).toBe(generalId);
  expect((await detail(generalId)).assignedTo).toBeNull();
  expect((await detail(assignedId)).assignedTo.email).toBe(email);
  const secondAgent = await post(page.request, base + '/ai-agents', {
    name: 'Second support',
    instructions: 'Answer briefly.',
  });
  await post(
    page.request,
    base + '/ai-agents/' + (await secondAgent.json()).id + '/activate',
    {},
    { 'If-Match': secondAgent.headers()['etag'] },
  );
  const english = await send('instagram', 'Please help with my order.', 'Second support');
  expect((await detail(english)).assignedTo.email).toBe(email);
  const noRule = await send('messenger', 'Please help with my order.', 'Second support');
  expect((await detail(noRule)).assignedTo).toBeNull();

  // Set every weekday explicitly so this browser journey works at any real time.
  await page.goto('/workspace-settings');
  await page.getByTestId('routing-settings').click();
  await page.getByTestId('hours-settings').click();
  await page.getByTestId('hours-enabled').check();
  const monday = page.locator('fieldset.day').filter({ has: page.locator('[data-day="1"]') });
  const times = monday.locator('input[type="time"]');
  await times.nth(1).fill('09:00');
  await expect(page.getByTestId('save-hours')).toBeDisabled();
  await times.nth(0).fill('22:00');
  await times.nth(1).fill('02:00');
  await expect(monday.locator('small')).toBeVisible();
  async function setWeek(mode: string) {
    for (let day = 0; day < 7; day++) await page.locator(`[data-day="${day}"]`).selectOption(mode);
    const [saved] = await Promise.all([
      page.waitForResponse(
        (r) =>
          new URL(r.url()).pathname.endsWith('/conversations/routing/hours') &&
          r.request().method() === 'PUT',
      ),
      page.getByTestId('save-hours').click(),
    ]);
    expect(saved.status()).toBe(204);
    await expect(page.getByTestId('save-hours')).toBeDisabled();
  }
  await page.getByTestId('after-hours-team').selectOption({ label: 'Owner' });
  await setWeek('closed');
  await page.reload();
  await page.getByTestId('routing-settings').click();
  await page.getByTestId('hours-settings').click();
  await expect(page.getByTestId('hours-enabled')).toBeChecked();
  await expect(page.getByTestId('after-hours-team').locator('option:checked')).toHaveText('Owner');
  await expect(page.locator('[data-day="0"]')).toHaveValue('closed');
  const after = await post(page.request, base + '/ai-agents', {
    name: 'Hours support',
    instructions: 'Answer briefly.',
  });
  await post(
    page.request,
    base + '/ai-agents/' + (await after.json()).id + '/activate',
    {},
    { 'If-Match': after.headers()['etag'] },
  );
  // Messenger has no active rule. The on-call role handles the first turn.
  const overnight = await send('messenger', '12345', 'Hours support');
  expect((await detail(overnight)).assignedTo.email).toBe(email);
  await page.goto('/workspace-settings');
  await page.getByTestId('routing-settings').click();
  await page.getByTestId('hours-settings').click();
  // The closed destination has nobody who can reply. During open hours Instagram's
  // English rule must still assign the owner.
  await page.getByTestId('after-hours-team').selectOption({ label: 'Support overflow' });
  await setWeek('all');
  const regular = await send('instagram', 'Hello, please help.', 'Hours support');
  expect((await detail(regular)).assignedTo.email).toBe(email);
  expect((await detail(overnight)).assignedTo.email).toBe(email);
  await info.attach('hours-assignment', {
    body: await page.screenshot(),
    contentType: 'image/png',
  });
});
