import { APIRequestContext, APIResponse } from '@playwright/test';

/** Sends a write the way the app does: with a fresh antiforgery token. */
export async function post(
  api: APIRequestContext,
  path: string,
  data: object,
  headers: Record<string, string> = {},
): Promise<APIResponse> {
  await api.get('/api/auth/csrf');

  const { cookies } = await api.storageState();
  const token = cookies.find((cookie) => cookie.name === 'XSRF-TOKEN')?.value;

  if (!token) {
    throw new Error('The API issued no antiforgery token.');
  }

  const response = await api.post(path, { data, headers: { 'X-XSRF-TOKEN': token, ...headers } });

  if (!response.ok()) {
    throw new Error(`${path} answered ${response.status()}`);
  }

  return response;
}
