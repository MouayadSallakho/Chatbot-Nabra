import { customerLabelPlain } from '@app/features/conversations/customer-label';
import { asLabelled, conversationsPhrase, initialOf } from './customer-words';

describe('asLabelled', () => {
  const record = (over: object) => ({
    id: 'p1',
    displayName: null,
    labelCode: 'AnonymousVisitor',
    shortCode: '7KQ2MX',
    ...over,
  });

  it('labels a directory record exactly as the inbox labels the same customer (BA-1)', () => {
    expect(customerLabelPlain(asLabelled(record({})))).toBe('Visitor 7KQ2MX');
    expect(customerLabelPlain(asLabelled(record({ labelCode: 'UnnamedCustomer' })))).toBe(
      'Customer 7KQ2MX',
    );
    expect(customerLabelPlain(asLabelled(record({ labelCode: 'SomethingNewer' })))).toBe(
      'Customer 7KQ2MX',
    );
  });

  it('shows a name somebody chose exactly as typed', () => {
    expect(
      customerLabelPlain(
        asLabelled(record({ displayName: '  Rania Haddad ', labelCode: 'Named' })),
      ),
    ).toBe('  Rania Haddad ');
  });

  it('tells an unnamed WhatsApp customer apart by the short code: the directory sends no number', () => {
    const labelled = asLabelled(record({ labelCode: 'WhatsAppCustomer' }));

    expect(labelled.reference).toBeNull();
    expect(customerLabelPlain(labelled)).toBe('WhatsApp customer 7KQ2MX');
  });

  it('keeps the record id, and nothing it was not given', () => {
    expect(Object.keys(asLabelled(record({}))).sort()).toEqual([
      'displayName',
      'id',
      'labelCode',
      'reference',
      'shortCode',
    ]);
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
