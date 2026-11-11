import { APIRequestContext, request } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { expect, signedInTest as test } from './support/fixtures';
import { assertLoopback } from './support/target';

/** The local mail sink's API (Phase 37.3). Loopback only, like everything here. */
const MAILPIT = 'http://127.0.0.1:8026';

/** The invitation email's subject in each language (`AccountEmailRenderer`). */
const SUBJECTS = {
  en: 'Nabra: You have been invited to a workspace',
  ar: 'نبرة: تلقّيت دعوة إلى مساحة عمل',
} as const;

interface MailpitSummary {
  readonly ID: string;
  readonly Subject: string;
}

/**
 * Waits for the one email sent to an address and returns its subject and the
 * language markers of its HTML.
 *
 * <strong>The body is never returned or printed:</strong> it carries the
 * invitation link, which is a secret. Only the subject and two booleans leave
 * this function.
 */
async function invitationEmail(
  mail: APIRequestContext,
  address: string,
): Promise<{ subject: string; arabic: boolean; english: boolean }> {
  for (let attempt = 0; attempt < 60; attempt++) {
    const search = await mail.get(`/api/v1/search?query=${encodeURIComponent(`to:"${address}"`)}`);
    const found: MailpitSummary[] = (await search.json()).messages ?? [];

    if (found.length > 0) {
      const message = await (await mail.get(`/api/v1/message/${found[0].ID}`)).json();
      const html: string = message.HTML ?? '';

      return {
        subject: found[0].Subject,
        arabic: html.includes('<html lang="ar" dir="rtl">'),
        english: html.includes('<html lang="en" dir="ltr">'),
      };
    }

    await new Promise((resolve) => setTimeout(resolve, 500));
  }

  throw new Error('No invitation email arrived in Mailpit.');
}

/**
 * An invitation's email language (Phase 38.4, D4) and the invitation lists
 * (D3). The form starts in the language the sender is using (the owner's
 * decision, 38.0 §4.3 #4) and always sends it; each project proves its own
 * language arrives in Mailpit. Then the invitation is revoked, which moves it
 * from the waiting list to the revoked one.
 */
test('an invitation email arrives in the language chosen, and revoking moves it between lists', async ({
  page,
  appLanguage,
}) => {
  assertLoopback(MAILPIT);

  const address = `e2e.invitee.${randomBytes(4).toString('hex')}@example.test`;
  const mail = await request.newContext({ baseURL: MAILPIT });

  try {
    await page.goto('/team/invite');

    await expect(page.getByTestId('invite-language')).toHaveValue(appLanguage);

    await page.locator('#invite-email').fill(address);

    const [created] = await Promise.all([
      page.waitForResponse(
        (response) =>
          response.request().method() === 'POST' && response.url().endsWith('/invitations'),
      ),
      page.getByTestId('create-invitation').click(),
    ]);

    // 201 Created, as the endpoint answers (its OpenAPI entry says 200: a backend ask).
    expect(created.status()).toBe(201);
    expect(created.request().postDataJSON().language).toBe(appLanguage);
    await expect(page.getByTestId('invitation-language')).toHaveAttribute('lang', appLanguage);

    const email = await invitationEmail(mail, address);

    expect(email.subject).toBe(SUBJECTS[appLanguage]);
    expect(email.arabic).toBe(appLanguage === 'ar');
    expect(email.english).toBe(appLanguage === 'en');

    // The waiting list: the invitation is there, with its total.
    await page.goto('/team');
    await expect(page.getByTestId('invitations-pending')).toHaveAttribute('aria-current', 'true');

    const row = page.getByTestId('invitations').locator('li', { hasText: address });

    await expect(row).toHaveCount(1);

    await row.getByTestId('revoke').click();
    await page.locator('dialog[open]').getByRole('button').last().click();
    await expect(page.getByTestId('invitations').locator('li', { hasText: address })).toHaveCount(
      0,
    );

    // The revoked list, by its link: the page and the list are in the address.
    await page.getByTestId('invitations-revoked').click();
    await expect(page).toHaveURL(/[?&]invitations=revoked/);
    await expect(page.getByTestId('invitations').locator('li', { hasText: address })).toHaveCount(
      1,
    );

    // A reload opens the same list.
    await page.reload();
    await expect(page.getByTestId('invitations-revoked')).toHaveAttribute('aria-current', 'true');
    await expect(page.getByTestId('invitations-total')).toBeVisible();
  } finally {
    await mail.dispose();
  }
});
