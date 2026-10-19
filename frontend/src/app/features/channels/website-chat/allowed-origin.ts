/** The server's own bound on a stored origin. */
const MAX_LENGTH = 255;

/** An origin the server would accept, or the reason it would not. */
export type OriginCheck = { readonly origin: string } | { readonly problem: string };

/**
 * Whether an origin is loopback, and therefore allowed to be plain HTTP.
 *
 * A developer embedding the widget on `http://localhost:3000` while building
 * their site is the one honest case for an unencrypted origin. Everything else
 * must be HTTPS, because an allowlist entry for an http site is an entry an
 * attacker on the same network can impersonate.
 */
function isLoopback(hostname: string): boolean {
  const host = hostname.toLowerCase();

  return (
    host === 'localhost' ||
    host.endsWith('.localhost') ||
    host === '127.0.0.1' ||
    host === '[::1]' ||
    host === '::1'
  );
}

/**
 * Checks and canonicalises one website address, the way the server will.
 *
 * <strong>This is a courtesy, not the boundary.</strong> The server validates
 * every origin again and is the only thing that decides what is stored; this
 * exists so somebody typing `example.com/support` is told what is wrong while
 * they are still looking at the box, rather than after a round trip. The rules
 * are kept deliberately identical so the two never disagree about a value —
 * see `WebChatOrigin` on the server, which is the definition.
 *
 * <strong>An origin is a scheme, a host and sometimes a port. Nothing
 * else.</strong> A path, a query, a fragment or credentials in the address all
 * mean somebody has pasted a page URL rather than named a site, and silently
 * trimming them would approve more than they read.
 */
export function checkOrigin(candidate: string): OriginCheck {
  const trimmed = candidate.trim();

  if (trimmed.length === 0) {
    return { problem: $localize`:@@origin.problem.empty:Enter a website address.` };
  }

  if (trimmed.length > MAX_LENGTH) {
    return {
      problem: $localize`:@@origin.problem.tooLong:A website address may be at most ${MAX_LENGTH}:max: characters.`,
    };
  }

  if (trimmed.includes('*')) {
    return {
      problem: $localize`:@@origin.problem.wildcard:Wildcards are not accepted. Add each website separately, including every subdomain the widget runs on.`,
    };
  }

  let parsed: URL;

  try {
    parsed = new URL(trimmed);
  } catch {
    return {
      problem: $localize`:@@origin.problem.notFull:Enter a full address, including https://.`,
    };
  }

  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    return {
      problem: $localize`:@@origin.problem.scheme:Only http and https addresses can be used.`,
    };
  }

  if (parsed.username.length > 0 || parsed.password.length > 0) {
    return {
      problem: $localize`:@@origin.problem.credentials:Remove the username and password from the address.`,
    };
  }

  if (parsed.pathname !== '' && parsed.pathname !== '/') {
    return {
      problem: $localize`:@@origin.problem.path:Remove the path: name the site itself, not a page on it.`,
    };
  }

  if (parsed.search.length > 0 || parsed.hash.length > 0) {
    return {
      problem: $localize`:@@origin.problem.query:Remove the query string and fragment from the address.`,
    };
  }

  if (parsed.hostname.length === 0) {
    return {
      problem: $localize`:@@origin.problem.notFull:Enter a full address, including https://.`,
    };
  }

  if (parsed.protocol === 'http:' && !isLoopback(parsed.hostname)) {
    return {
      problem: $localize`:@@origin.problem.http:Use https. Plain http is only accepted for localhost.`,
    };
  }

  // `URL.origin` is already the canonical form: lower-cased scheme and host, the
  // punycode form of an international domain, and the default port omitted. Using
  // it rather than reassembling the parts is what keeps this identical to the
  // browser's own idea of an origin, which is the value the widget will report.
  return { origin: parsed.origin };
}
