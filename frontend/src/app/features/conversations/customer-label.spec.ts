import { TestBed } from '@angular/core/testing';
import type { ConversationCustomer } from './conversations-api';
import { CustomerLabel, customerLabel, customerLabelPlain } from './customer-label';

describe('customerLabel', () => {
  const customer = (overrides: Partial<ConversationCustomer>): ConversationCustomer => ({
    id: 'c1',
    displayName: null,
    labelCode: 'AnonymousVisitor',
    reference: null,
    shortCode: '7KQ2MX',
    ...overrides,
  });

  it('uses a name somebody gave, exactly as typed, whatever the code', () => {
    expect(customerLabel(customer({ displayName: 'Lina Haddad', labelCode: 'Named' }))).toEqual({
      kind: 'name',
      name: 'Lina Haddad',
    });
  });

  it('calls an anonymous visitor a visitor, with the short code', () => {
    expect(customerLabel(customer({}))).toEqual({
      kind: 'label',
      word: 'Visitor',
      code: '7KQ2MX',
    });
  });

  it('calls a WhatsApp customer that, with the masked number rather than the short code', () => {
    expect(
      customerLabel(customer({ labelCode: 'WhatsAppCustomer', reference: '••••1234' })),
    ).toEqual({ kind: 'label', word: 'WhatsApp customer', code: '••••1234' });
  });

  it('falls back to the short code for a WhatsApp customer with no number', () => {
    expect(customerLabel(customer({ labelCode: 'WhatsAppCustomer' }))).toEqual({
      kind: 'label',
      word: 'WhatsApp customer',
      code: '7KQ2MX',
    });
  });

  it('calls an unnamed customer a customer', () => {
    expect(customerLabel(customer({ labelCode: 'UnnamedCustomer' }))).toEqual({
      kind: 'label',
      word: 'Customer',
      code: '7KQ2MX',
    });
  });

  it('reads a code this build does not know as a plain customer', () => {
    expect(customerLabel(customer({ labelCode: 'SomethingNew' }))).toEqual({
      kind: 'label',
      word: 'Customer',
      code: '7KQ2MX',
    });
  });

  it('does not treat a blank name as a name', () => {
    expect(customerLabel(customer({ displayName: '   ' }))).toEqual({
      kind: 'label',
      word: 'Visitor',
      code: '7KQ2MX',
    });
  });

  it('says only "Customer" for a conversation with no customer on it', () => {
    expect(customerLabel(null)).toEqual({ kind: 'label', word: 'Customer', code: null });
  });
});

describe('customerLabelPlain', () => {
  const customer: ConversationCustomer = {
    id: 'c1',
    displayName: null,
    labelCode: 'AnonymousVisitor',
    reference: null,
    shortCode: '7KQ2MX',
  };

  it('writes the word and the code as one line', () => {
    expect(customerLabelPlain(customer)).toBe('Visitor 7KQ2MX');
  });

  it('writes a name as it is', () => {
    expect(customerLabelPlain({ ...customer, displayName: 'Lina' })).toBe('Lina');
  });

  it('writes only the word when there is no customer', () => {
    expect(customerLabelPlain(null)).toBe('Customer');
  });
});

describe('CustomerLabel', () => {
  function render(customer: ConversationCustomer | null): HTMLElement {
    const fixture = TestBed.createComponent(CustomerLabel);

    fixture.componentRef.setInput('customer', customer);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  const base: ConversationCustomer = {
    id: 'c1',
    displayName: null,
    labelCode: 'AnonymousVisitor',
    reference: null,
    shortCode: '7KQ2MX',
  };

  it('lets a name take its own direction', () => {
    const island = render({ ...base, displayName: 'لينا حداد' }).querySelector('bdi');

    expect(island?.textContent).toBe('لينا حداد');
    expect(island?.getAttribute('dir')).toBe('auto');
  });

  it('keeps a short code left to right, untranslated, with a space before it', () => {
    const host = render(base);
    const island = host.querySelector('bdi');

    expect(host.textContent?.replace(/\s+/g, ' ').trim()).toBe('Visitor 7KQ2MX');
    expect(island?.getAttribute('dir')).toBe('ltr');
    expect(island?.getAttribute('translate')).toBe('no');
  });

  it('keeps a masked number left to right too', () => {
    const island = render({
      ...base,
      labelCode: 'WhatsAppCustomer',
      reference: '••••1234',
    }).querySelector('bdi');

    expect(island?.textContent).toBe('••••1234');
    expect(island?.getAttribute('dir')).toBe('ltr');
  });

  it('renders a name as text, never as markup', () => {
    const host = render({ ...base, displayName: '<img src=x onerror=alert(1)>' });

    expect(host.querySelector('img')).toBeNull();
    expect(host.textContent).toContain('<img src=x onerror=alert(1)>');
  });

  it('draws no island when there is nothing to tell customers apart', () => {
    const host = render(null);

    expect(host.textContent?.trim()).toBe('Customer');
    expect(host.querySelector('bdi')).toBeNull();
  });
});
