import { Page } from '@playwright/test';
import { put } from './support/api';
import { e2eWorkspace, expect, signedInTest as test } from './support/fixtures';

/** Puts the run's workspace back to UTC through the API, whatever a journey left behind. */
async function backToUtc(page: Page): Promise<void> {
  const { tenantId } = e2eWorkspace();
  const read = await page.request.get(`/api/tenants/${tenantId}`);
  const tenant = (await read.json()) as { timeZone: string };

  if (tenant.timeZone !== 'UTC') {
    await put(
      page.request,
      `/api/tenants/${tenantId}/time-zone`,
      { timeZone: 'UTC' },
      { 'If-Match': read.headers()['etag'] ?? '' },
    );
  }
}

/** Chooses a zone on the settings page, saves it, and returns the PUT's status. */
async function saveZone(page: Page, filter: string, zone: string): Promise<number> {
  await page.getByTestId('time-zone-filter').fill(filter);
  await page.getByTestId('time-zone-select').selectOption(zone);

  const [response] = await Promise.all([
    page.waitForResponse(
      (candidate) =>
        candidate.request().method() === 'PUT' && candidate.url().endsWith('/time-zone'),
    ),
    page.getByTestId('save-zone').click(),
  ]);

  return response.status();
}

/**
 * The workspace time zone (Phase 38.5, E1 and E2): changing it on the settings
 * page moves the analytics days, the comparison and the report period hint to
 * the new zone, on the next read and with no other change. The analytics page
 * asks for the comparison on its one dashboard read. The run's workspace is put
 * back to UTC at the end, and again through the API if anything failed first.
 */
test('a time-zone change moves the analytics and report labels to the new zone', async ({
  page,
}) => {
  try {
    await page.goto('/workspace-settings');
    await expect(page.locator('h1')).toHaveCount(1);
    await expect(page.getByTestId('time-zone-select')).toHaveValue('UTC');

    // The run's workspace has no schedules, so nothing asks before saving.
    expect(await saveZone(page, 'riyadh', 'Asia/Riyadh')).toBe(200);
    await expect(page.locator('dialog[open]')).toHaveCount(0);
    await expect(page.locator('.toast-region[role="status"]')).toContainText('Asia/Riyadh');

    // Analytics: one dashboard read, carrying the comparison, labelled in the new zone.
    const reads: string[] = [];

    page.on('request', (request) => {
      if (new URL(request.url()).pathname.endsWith('/analytics/dashboard')) {
        reads.push(request.url());
      }
    });

    await page.goto('/analytics');
    await expect(page.getByTestId('period')).toContainText('Asia/Riyadh');
    await expect(page.getByTestId('comparison')).toBeVisible();
    await expect(page.getByTestId('since')).toBeVisible();
    await expect(page.getByTestId('summary-conversations-change')).toBeVisible();
    expect(reads).toHaveLength(1);
    expect(new URL(reads[0]).searchParams.get('compare')).toBe('previous');

    // Reports: the period hint says the same zone.
    await page.goto('/reports');
    await expect(page.getByTestId('report-range-hint')).toContainText('Asia/Riyadh');

    // And back, through the page, so the next journey meets UTC.
    await page.goto('/workspace-settings');
    expect(await saveZone(page, 'utc', 'UTC')).toBe(200);

    await page.goto('/analytics');
    await expect(page.getByTestId('period')).not.toContainText('Asia/Riyadh');
  } finally {
    await backToUtc(page);
  }
});
