// Target guard for every Node-based performance tool in this directory.
//
// NON-PRODUCTION TOOLING. These scripts generate load and inject failures; they
// must never be pointed at a shared, staging or production environment. The
// allow-list below is deliberately hard-coded to loopback addresses: widening it
// is a code change somebody has to make on purpose and a reviewer has to accept,
// not an environment variable that can be set by accident.

const ALLOWED_HOSTS = new Set(['localhost', '127.0.0.1', '[::1]', '::1']);

/**
 * Returns a validated base URL for the system under test.
 *
 * @param {string | undefined} value Base URL, e.g. http://127.0.0.1:5034
 * @param {string} fallback Loopback default used when nothing is supplied.
 * @returns {string} The base URL without a trailing slash.
 */
export function loopbackBase(value, fallback = 'http://127.0.0.1:5034') {
  const raw = value && value.trim() !== '' ? value.trim() : fallback;
  let url;

  try {
    url = new URL(raw);
  } catch {
    throw new Error(`Refusing target "${raw}": not a URL.`);
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw new Error(`Refusing target "${raw}": only http(s) is supported.`);
  }

  if (!ALLOWED_HOSTS.has(url.hostname)) {
    throw new Error(
      `Refusing target "${url.hostname}": performance tools only run against loopback hosts.`,
    );
  }

  return url.origin;
}
