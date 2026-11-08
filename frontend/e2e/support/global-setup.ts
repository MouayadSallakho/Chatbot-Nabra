import { FullConfig, request } from '@playwright/test';
import { randomBytes } from 'node:crypto';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';
import { post } from './api';
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
 * and removed before launch. It gets an active agent and a website chat, so a
 * journey can have a visitor write in.
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
    await post(api, '/api/auth/register', { email, password, language: 'en' });

    const tenant = await (
      await post(api, '/api/tenants', { name: `E2E ${runId}`, slug: `e2e-${runId}` })
    ).json();
    const base = `/api/tenants/${tenant.id}`;

    const agent = await post(api, `${base}/ai-agents`, {
      name: 'E2E agent',
      instructions: 'Answer the visitor briefly.',
    });
    const agentId = (await agent.json()).id;

    await post(api, `${base}/ai-agents/${agentId}/activate`, {}, ifMatch(agent));

    const chat = await post(api, `${base}/website-chat`, { aiAgentId: agentId });
    const created = await chat.json();

    await post(api, `${base}/website-chat/${created.id}/activate`, {}, ifMatch(chat));

    await api.storageState({ path: join(stateDir, 'owner.json') });

    process.env['NABRA_E2E_TENANT_ID'] = tenant.id;
    process.env['NABRA_E2E_CHAT_KEY'] = await publicKey(api, base, created.id);
  } finally {
    await api.dispose();
  }

  process.env['NABRA_E2E_EMAIL'] = email;
  process.env['NABRA_E2E_PASSWORD'] = password;
  process.env['NABRA_E2E_THROTTLE_EMAIL'] = `e2e.throttle.${runId}@example.test`;
  process.env['NABRA_E2E_STATE_DIR'] = stateDir;
}

function ifMatch(response: { headers(): Record<string, string> }): Record<string, string> {
  return { 'If-Match': response.headers()['etag'] };
}

async function publicKey(
  api: Awaited<ReturnType<typeof request.newContext>>,
  base: string,
  endpointId: string,
): Promise<string> {
  const listed = await (await api.get(`${base}/website-chat`)).json();
  const endpoints: { id: string; publicKey: string }[] = listed.items ?? listed;
  const key = endpoints.find((endpoint) => endpoint.id === endpointId)?.publicKey;

  if (!key) {
    throw new Error('The website chat has no public key.');
  }

  return key;
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
