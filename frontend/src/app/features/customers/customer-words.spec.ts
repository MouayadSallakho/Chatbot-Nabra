import { conversationsPhrase, customerName, initialOf } from './customer-words';

describe('customerName', () => {
  it('shows a name somebody chose exactly as typed', () => {
    expect(customerName({ displayName: '  Rania Haddad ', channels: ['WebChat'] })).toBe(
      '  Rania Haddad ',
    );
  });

  // The same rule, in the same order, as the server's CustomerLabels.For, so the
  // English screen says exactly what the API's label says.
  it('names an unnamed customer from how they reach the workspace, as the server does', () => {
    expect(customerName({ displayName: null, channels: ['WhatsApp'] })).toBe('WhatsApp customer');
    expect(customerName({ displayName: null, channels: ['WebChat'] })).toBe('Anonymous visitor');
    expect(customerName({ displayName: null, channels: [] })).toBe('Anonymous visitor');
    expect(customerName({ displayName: null, channels: ['WebChat', 'WhatsApp'] })).toBe(
      'Unnamed customer',
    );
  });
});

describe('conversationsPhrase', () => {
  it('counts in words, with a plural form for every count', () => {
    expect(conversationsPhrase('en', 1)).toBe('1 conversation');
    expect(conversationsPhrase('en', 3)).toBe('3 conversations');
    expect(conversationsPhrase('en', 1200)).toBe('1,200 conversations');
  });
});

describe('initialOf', () => {
  it('takes the first letter, as a whole grapheme', () => {
    expect(initialOf('rania')).toBe('R');
    expect(initialOf('Élodie')).toBe('É');
    expect(initialOf('ليلى')).toBe('ل');
  });

  it('skips punctuation and markup-looking characters before the first letter', () => {
    expect(initialOf('<b>Bold Customer</b>')).toBe('B');
    expect(initialOf('"Sam"')).toBe('S');
  });

  it('gives nothing when there is no letter at all', () => {
    expect(initialOf('1234')).toBe('');
    expect(initialOf('')).toBe('');
  });
});
