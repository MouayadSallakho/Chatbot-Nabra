import { checkOrigin } from './allowed-origin';

describe('checkOrigin', () => {
  const accepted = (candidate: string): string => {
    const checked = checkOrigin(candidate);

    if ('problem' in checked) {
      throw new Error(`expected '${candidate}' to be accepted, but: ${checked.problem}`);
    }

    return checked.origin;
  };

  const refused = (candidate: string): string => {
    const checked = checkOrigin(candidate);

    if (!('problem' in checked)) {
      throw new Error(`expected '${candidate}' to be refused, but it became ${checked.origin}`);
    }

    return checked.problem;
  };

  it('accepts an ordinary https site', () => {
    expect(accepted('https://www.example.com')).toBe('https://www.example.com');
  });

  it('canonicalises case, the default port and a bare trailing slash', () => {
    // All four of these name one website. Storing them as four entries would
    // invite somebody to "remove" one that was never separately allowed.
    expect(accepted('HTTPS://WWW.Example.COM')).toBe('https://www.example.com');
    expect(accepted('https://www.example.com:443')).toBe('https://www.example.com');
    expect(accepted('https://www.example.com/')).toBe('https://www.example.com');
    expect(accepted('  https://www.example.com  ')).toBe('https://www.example.com');
  });

  it('keeps a non-default port, because it is part of the origin', () => {
    expect(accepted('https://www.example.com:8443')).toBe('https://www.example.com:8443');
  });

  it('converts an international domain to the form a browser reports', () => {
    // event.origin carries the punycode host, so storing the unicode spelling
    // would produce an entry nothing could ever match.
    expect(accepted('https://xn--bcher-kva.example')).toBe('https://xn--bcher-kva.example');
    expect(accepted('https://bücher.example')).toBe('https://xn--bcher-kva.example');
  });

  it('refuses a wildcard outright rather than interpreting it', () => {
    expect(refused('https://*.example.com')).toContain('Wildcards');
    expect(refused('*')).toContain('Wildcards');
  });

  it('refuses a path, a query and a fragment', () => {
    // Each of these means somebody pasted a page address rather than naming a
    // site. Trimming it silently would approve more than they read.
    expect(refused('https://www.example.com/support')).toContain('path');
    expect(refused('https://www.example.com/?utm=1')).toContain('query');
    expect(refused('https://www.example.com/#chat')).toContain('query');
  });

  it('refuses credentials in the address', () => {
    expect(refused('https://user:secret@example.com')).toContain('username');
  });

  it('refuses plain http except on loopback', () => {
    expect(refused('http://www.example.com')).toContain('https');
    expect(accepted('http://localhost:3000')).toBe('http://localhost:3000');
    expect(accepted('http://127.0.0.1:8080')).toBe('http://127.0.0.1:8080');
    expect(accepted('http://app.localhost:4200')).toBe('http://app.localhost:4200');
  });

  it('refuses a scheme that is not http or https', () => {
    for (const candidate of [
      'ftp://example.com',
      'file:///etc/passwd',
      'javascript:alert(1)',
      'data:text/html,<script>alert(1)</script>',
    ]) {
      expect(refused(candidate)).toBeTruthy();
    }
  });

  it('refuses a bare hostname, because it names no scheme', () => {
    expect(refused('example.com')).toContain('https://');
  });

  it('refuses the opaque origin and the empty string', () => {
    expect(refused('null')).toBeTruthy();
    expect(refused('')).toContain('Enter a website address');
    expect(refused('   ')).toContain('Enter a website address');
  });

  it('refuses an address longer than the server will store', () => {
    expect(refused(`https://${'a'.repeat(260)}.example.com`)).toContain('255');
  });
});
