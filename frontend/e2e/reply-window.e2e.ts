import {
  e2eLongConversation,
  e2eWorkspace,
  expect,
  signedInTest as test,
} from './support/fixtures';

test('a simulated WhatsApp deadline disables sending and preserves the draft', async ({
  page,
  appLanguage,
}) => {
  const id = e2eLongConversation();
  const { tenantId } = e2eWorkspace();
  const base = `/api/tenants/${tenantId}`;
  let open = false;
  await page.route('**' + base + '/whatsapp/conversations/' + id, (route) =>
    route.fulfill({ json: { customerReference: '••42', deliveryStatuses: {} } }),
  );
  await page.route('**' + base + '/conversations/' + id, async (route) => {
    const response = await route.fetch();
    const now = Date.now();
    await route.fulfill({
      response,
      json: {
        ...(await response.json()),
        channel: 'WhatsApp',
        handling: 'Human',
        replyWindow: {
          isOpen: open,
          checkedAt: new Date(now).toISOString(),
          closesAt: open ? new Date(now + 8000).toISOString() : null,
        },
      },
    });
  });
  await page.goto('/conversations/' + id);
  const composer = page.locator('#conversation-reply');
  const send = page.getByTestId('send-reply');
  const status = page.getByTestId('reply-window');
  await composer.fill('Draft');
  await expect(send).toBeDisabled();
  await expect(status).toContainText(appLanguage === 'ar' ? 'انتهت' : 'closed');
  open = true;
  await page.getByTestId('refresh-conversation').click();
  await expect(send).toBeEnabled();
  await expect(status).toContainText(appLanguage === 'ar' ? 'المتبقي' : 'remaining');
  await expect(send).toBeDisabled({ timeout: 15000 });
  await expect(composer).toHaveValue('Draft');
});
