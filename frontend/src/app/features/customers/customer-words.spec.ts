import { customerLabelPlain } from '@app/features/conversations/customer-label';
import { asLabelled, conversationsPhrase, customerAvatar } from './customer-words';

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

  it('labels an unnamed WhatsApp customer by its masked number, as the inbox does (39.1)', () => {
    const labelled = asLabelled(record({ labelCode: 'WhatsAppCustomer', reference: '••••4821' }));

    expect(labelled.reference).toBe('••••4821');
    expect(customerLabelPlain(labelled)).toBe('WhatsApp customer ••••4821');
  });

  it('falls back to the short code when a read carries no masked number', () => {
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
    expect(conversationsPhrase('en', 0)).toBe('No conversations');
    expect(conversationsPhrase('en', 1)).toBe('1 conversation');
    expect(conversationsPhrase('en', 3)).toBe('3 conversations');
    expect(conversationsPhrase('en', 1200)).toBe('1,200 conversations');
  });
});

describe('customerAvatar', () => {
  const record = (over: object) =>
    asLabelled({
      id: 'p1',
      displayName: null,
      labelCode: 'AnonymousVisitor',
      shortCode: '7KQ2MX',
      ...over,
    });

  it("draws a name's initials, or the label code's first two characters, with the first channel", () => {
    expect(
      customerAvatar(record({ displayName: 'Rania Haddad', labelCode: 'Named' }), ['WhatsApp']),
    ).toEqual({
      name: 'Rania Haddad',
      text: '',
      channel: 'WhatsApp',
    });
    expect(customerAvatar(record({}), ['WebChat', 'WhatsApp'])).toEqual({
      name: 'Visitor 7KQ2MX',
      text: '7K',
      channel: 'WebChat',
    });
    expect(
      customerAvatar(record({ labelCode: 'WhatsAppCustomer', reference: '••••4821' }), []).text,
    ).toBe('48');
    expect(customerAvatar(record({}), []).channel).toBe('');
  });
});
