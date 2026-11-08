import { FullConfig, request } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { API_READY_URL, BASE_URL, assertLoopback } from './target';

/**
 * Prepares one run: checks the stack, and creates a fresh synthetic account.
 *
 * <strong>No password is written anywhere.</strong> It is generated here, kept in
 * this process's environment (which the workers inherit) and gone when the run
 * ends. Only the cookie state is saved, under the folder the run names, and
 * teardown deletes it.
 *
 * The account's workspace is named "E2E" plus the run's id, so it can be recognised
 * and removed before launch.
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
  assertLoopback(BASE_URL);
  assertLoopback(API_READY_URL);

  const runId = randomBytes(5).toString('hex');
  const email = `e2e.${runId}@example.test`;
  const password = randomBytes(24).toString('base64url');
  const stateDir = process.env['NABRA_E2E_STATE_DIR'] ?? join(config.rootDir, '.e2e-state');

  mkdirSync(stateDir, { recursive: true });

  await expectUp(BASE_URL, 'the dev server');
  await expectUp(API_READY_URL, 'the API');

  // Through the dev server's proxy, as a browser would, so the antiforgery cookies
  // belong to the same origin the pages use.
  const api = await request.newContext({ baseURL: BASE_URL });

  try {
    const send = async (path: string, data: object): Promise<void> => {
      const response = await api.post(path, {
        data,
        headers: { 'X-XSRF-TOKEN': await antiforgeryToken() },
      });

      if (!response.ok()) {
        throw new Error(`${path} answered ${response.status()}`);
      }
    };
    const antiforgeryToken = async (): Promise<string> => {
      await api.get('/api/auth/csrf');

      const { cookies } = await api.storageState();
      const token = cookies.find((cookie) => cookie.name === 'XSRF-TOKEN')?.value;

      if (!token) {
        throw new Error('The API issued no antiforgery token.');
      }

      return token;
    };

    await send('/api/auth/register', { email, password, language: 'en' });
    await send('/api/tenants', { name: `E2E ${runId}`, slug: `e2e-${runId}` });

    await api.storageState({ path: join(stateDir, 'owner.json') });
  } finally {
    await api.dispose();
  }

  process.env['NABRA_E2E_EMAIL'] = email;
  process.env['NABRA_E2E_PASSWORD'] = password;
  process.env['NABRA_E2E_THROTTLE_EMAIL'] = `e2e.throttle.${runId}@example.test`;
  process.env['NABRA_E2E_STATE_DIR'] = stateDir;
}

async function expectUp(url: string, what: string): Promise<void> {
  const probe = await request.newContext();

  try {
    const response = await probe.get(url, { timeout: 10_000 });

    if (!response.ok()) {
      throw new Error(`${what} at ${url} answered ${response.status()}.`);
    }
  } finally {
    await probe.dispose();
  }
}
