import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { WhatsAppConversationFacts } from '@app/features/channels/whatsapp-api';
import type { ConversationCustomer, ConversationDetail } from '../conversations-api';
import { ConversationContext } from './conversation-context';

describe('ConversationContext', () => {
  let fixture: ComponentFixture<ConversationContext>;

  const conversation = (customer: ConversationCustomer | null): ConversationDetail => ({
    id: 'c1',
    channel: 'WhatsApp',
    status: 'Open',
    handling: 'Ai',
    startedAt: '2026-09-07T10:00:00Z',
    lastActivityAt: '2026-09-07T10:05:00Z',
    agentName: 'Concierge',
    messageCount: 2,
    assignedTo: null,
    customer,
    lastReadSequence: null,
  });

  const whatsApp = (over: Partial<ConversationCustomer>): ConversationCustomer => ({
    id: 'p1',
    displayName: null,
    labelCode: 'WhatsAppCustomer',
    reference: '••••1234',
    shortCode: '7KQ2MX',
    ...over,
  });

  function mount(
    detail: ConversationDetail,
    facts: WhatsAppConversationFacts | null = null,
  ): HTMLElement {
    fixture = TestBed.createComponent(ConversationContext);
    fixture.componentRef.setInput('conversation', detail);
    fixture.componentRef.setInput('whatsApp', facts);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  const customerLine = (host: HTMLElement) =>
    host
      .querySelector('[data-testid="context-customer"]')
      ?.textContent?.replace(/\s+/g, ' ')
      .trim();

  it('names the customer the way the rows and the heading do', () => {
    expect(customerLine(mount(conversation(whatsApp({}))))).toBe('WhatsApp customer ••••1234');
  });

  it('does not repeat the number when the label already is the number', () => {
    const host = mount(conversation(whatsApp({})));

    expect(host.textContent).not.toContain('Masked number');
    expect(host.querySelectorAll('bdi')).toHaveLength(1);
  });

  it('shows the masked number beside a name, in its own left-to-right island', () => {
    const host = mount(conversation(whatsApp({ displayName: 'Lina Haddad' })));
    const number = [...host.querySelectorAll('bdi')].find(
      (node) => node.textContent === '••••1234',
    );

    expect(customerLine(host)).toBe('Lina Haddad');
    expect(host.textContent).toContain('Masked number');
    expect(number?.getAttribute('dir')).toBe('ltr');
  });

  it('still shows the WhatsApp number when the conversation has no customer on it', () => {
    const facts = { customerReference: '••••9876', deliveryStatuses: {} };
    const host = mount(conversation(null), facts as unknown as WhatsAppConversationFacts);

    expect(customerLine(host)).toBe('Customer');
    expect(host.textContent).toContain('••••9876');
  });
});
