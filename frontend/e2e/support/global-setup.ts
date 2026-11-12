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
 * and removed before launch. It gets two active agents, each with its own website
 * chat, so journeys can have a visitor write in.
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

    const chatKey = await agentWithChat(api, base, 'E2E agent');

    // A second chat link, for the journeys where a visitor waits or a person steps
    // in. The public chat counts new conversations per network *and per link*
    // (five in five minutes), and the first link's five are already spent by the
    // journeys above; one link per agent, so a second agent.
    const handoverChatKey = await agentWithChat(api, base, 'E2E handover agent');

    await api.storageState({ path: join(stateDir, 'owner.json') });

    process.env['NABRA_E2E_TENANT_ID'] = tenant.id;
    process.env['NABRA_E2E_CHAT_KEY'] = chatKey;
    process.env['NABRA_E2E_HANDOVER_CHAT_KEY'] = handoverChatKey;
    process.env['NABRA_E2E_LONG_CONVERSATION'] = await longConversation(api, base, chatKey);
  } finally {
    await api.dispose();
  }

  process.env['NABRA_E2E_EMAIL'] = email;
  process.env['NABRA_E2E_PASSWORD'] = password;
  process.env['NABRA_E2E_THROTTLE_EMAIL'] = `e2e.throttle.${runId}@example.test`;
  process.env['NABRA_E2E_STATE_DIR'] = stateDir;
}

/** An active agent with an active website chat of its own; returns the chat's public key. */
async function agentWithChat(
  api: Awaited<ReturnType<typeof request.newContext>>,
  base: string,
  name: string,
): Promise<string> {
  const agent = await post(api, `${base}/ai-agents`, {
    name,
    instructions: 'Answer the visitor briefly.',
  });
  const agentId = (await agent.json()).id;

  await post(api, `${base}/ai-agents/${agentId}/activate`, {}, ifMatch(agent));

  const chat = await post(api, `${base}/website-chat`, { aiAgentId: agentId });
  const created = await chat.json();

  await post(api, `${base}/website-chat/${created.id}/activate`, {}, ifMatch(chat));

  return publicKey(api, base, created.id);
}

/** More than one window of messages, so the older ones have to be read. */
const LONG_CONVERSATION_REPLIES = 49;

/**
 * One conversation longer than a window, built once for the whole run.
 *
 * Once, and not per journey: the public chat allows five new conversations per
 * network every five minutes, and the journeys that need a live visitor use those.
 * Replies come from a person, because those are not capped per conversation.
 */
async function longConversation(
  api: Awaited<ReturnType<typeof request.newContext>>,
  base: string,
  chatKey: string,
): Promise<string> {
  const visitor = await request.newContext({ baseURL: BASE_URL });

  try {
    const started = await post(visitor, `/api/public/webchat/${chatKey}/conversations`, {});
    const { conversationId } = await started.json();
    const path = `${base}/conversations/${conversationId}`;

    await post(visitor, `/api/public/webchat/conversations/${conversationId}/messages`, {
      clientMessageId: crypto.randomUUID(),
      text: 'Opening message',
    });
    await untilAiHasReplied(api, path);

    await post(api, `${path}/take-over`, {}, ifMatch(await api.get(path)));

    for (let reply = 1; reply <= LONG_CONVERSATION_REPLIES; reply++) {
      await post(api, `${path}/messages`, {
        clientMessageId: crypto.randomUUID(),
        text: `Reply number ${reply}`,
      });
    }

    return conversationId;
  } finally {
    await visitor.dispose();
  }
}

/** So the transcript has stopped growing before a person takes over. */
async function untilAiHasReplied(
  api: Awaited<ReturnType<typeof request.newContext>>,
  path: string,
): Promise<void> {
  for (let attempt = 0; attempt < 80; attempt++) {
    if ((await (await api.get(path)).json()).messageCount >= 2) {
      return;
    }

    await new Promise((resolve) => setTimeout(resolve, 250));
  }

  throw new Error('The AI stand-in did not reply. Is it switched on?');
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
