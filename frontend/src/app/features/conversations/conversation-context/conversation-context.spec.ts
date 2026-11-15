import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Subject } from 'rxjs';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import {
  Customer,
  CustomerConversation,
  CustomersApi,
  VersionedCustomer,
} from '@app/features/customers/customers-api';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
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
    unread: false,
    takenOverBy: null,
    needsPerson: false,
    needsPersonReason: null,
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

  describe('the customer read (39.4b)', () => {
    const tenantId = '11111111-1111-7111-8111-111111111111';
    let reply: Subject<VersionedCustomer>;
    let reads: string[];

    const older = (id: string, day: number): CustomerConversation => ({
      id,
      channel: 'WebChat',
      status: 'Closed',
      handling: 'Ai',
      startedAt: `2026-09-0${day}T09:00:00Z`,
      lastActivityAt: `2026-09-0${day}T09:30:00Z`,
    });

    const customer = (over: Partial<Customer> = {}): Customer => ({
      id: 'p1',
      displayName: null,
      label: 'Visitor',
      labelCode: 'AnonymousVisitor',
      shortCode: '7KQ2MX',
      reference: null,
      identities: [],
      channels: ['WebChat'],
      conversationCount: 6,
      lastActivityAt: null,
      createdAt: '2026-09-03T08:00:00Z',
      updatedAt: '2026-09-03T08:00:00Z',
      mergedIntoCustomerId: null,
      mergedAt: null,
      canReadConversations: true,
      conversations: [older('c1', 9), older('a', 8), older('b', 7), older('c', 6), older('d', 5)],
      ...over,
    });

    async function open(...permissions: string[]): Promise<HTMLElement> {
      reads = [];
      reply = new Subject<VersionedCustomer>();
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          // `adopt` remembers the workspace; keep it out of the real localStorage.
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
          { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
          {
            provide: CustomersApi,
            useValue: {
              get: (_tenant: string, id: string) => {
                reads.push(id);

                return reply;
              },
            },
          },
        ],
      });
      await TestBed.inject(WorkspaceContextService).adopt({
        id: tenantId,
        name: 'Acme',
        slug: 'acme',
        status: 'Active',
      });

      const host = mount(
        conversation(whatsApp({ labelCode: 'AnonymousVisitor', reference: null })),
      );

      await fixture.whenStable();
      fixture.detectChanges();

      return host;
    }

    const settle = async (): Promise<void> => {
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
    };

    it('reads the customer once, and shows since when, how many and the newest others', async () => {
      const host = await open('customers.view');

      expect(reads).toEqual(['p1']);
      expect(host.querySelector('[role="status"]')?.textContent).toContain('Loading the customer');
      // The loaded shapes are kept while it reads: the count's tile, a line and the link.
      expect(host.querySelectorAll('.customer-tile')).toHaveLength(2);
      expect(host.querySelector('.customer-tile .figure-skeleton')).not.toBeNull();
      expect(host.querySelector('.customer-card .since-skeleton')).not.toBeNull();
      expect(
        host.querySelectorAll(
          '.context-section .line-skeleton, .context-section .earlier-skeleton',
        ),
      ).toHaveLength(2);

      reply.next({ customer: customer(), version: '"v1"' });
      await settle();

      expect(host.querySelector('[data-testid="customer-since"]')?.textContent?.trim()).toMatch(
        /^Customer since September \d, 2026$/,
      );
      expect(host.querySelector('[data-testid="customer-conversations"]')?.textContent).toBe('6');

      // The open conversation (c1) is not earlier; three are listed, newest first.
      const rows = [
        ...host.querySelectorAll<HTMLAnchorElement>('[data-testid="earlier-conversation"]'),
      ];

      expect(rows.map((row) => row.getAttribute('href'))).toEqual([
        '/conversations/a',
        '/conversations/b',
        '/conversations/c',
      ]);
      expect(rows[0].textContent?.replace(/\s+/g, ' ').trim()).toMatch(
        /^Website chat · Closed, Sep \d, 2026$/,
      );
      expect(host.querySelector('[data-testid="customer-page-link"]')?.textContent?.trim()).toBe(
        'See all their conversations',
      );
      expect(host.querySelector('[data-testid="customer-page-link"]')?.getAttribute('href')).toBe(
        '/customers/p1',
      );

      // The same customer again: no second read.
      fixture.componentRef.setInput('conversation', {
        ...conversation(whatsApp({ labelCode: 'AnonymousVisitor', reference: null })),
        messageCount: 3,
      });
      await settle();
      expect(reads).toEqual(['p1']);
    });

    it('says when the customer has no other conversation', async () => {
      const host = await open('customers.view');

      reply.next({
        customer: customer({ conversationCount: 1, conversations: [older('c1', 9)] }),
        version: '"v1"',
      });
      await settle();

      expect(host.textContent).toContain('No earlier conversations.');
      expect(host.querySelector('[data-testid="customer-page-link"]')?.textContent?.trim()).toBe(
        "Open the customer's page",
      );
    });

    it('says a failed read quietly, keeps the rest, and reads again when asked', async () => {
      const host = await open('customers.view');

      reply.error(new Error('offline'));
      await settle();

      expect(host.querySelector('[data-testid="customer-failed"]')?.textContent).toContain(
        "Could not load this customer's other conversations.",
      );
      expect(host.querySelector('[data-testid="customer-channel"]')?.textContent).toBe('WhatsApp');

      reply = new Subject<VersionedCustomer>();
      host.querySelector<HTMLButtonElement>('.context-retry')!.click();
      await settle();

      expect(reads).toEqual(['p1', 'p1']);
    });

    it('asks nothing without customers.view, and shows the conversation facts all the same', async () => {
      const host = await open();

      expect(reads).toEqual([]);
      expect(host.querySelector('[data-testid="customer-earlier"]')).toBeNull();
      expect(host.querySelector('[data-testid="customer-since"]')).toBeNull();
      expect(host.querySelector('[data-testid="context-assignee"]')?.textContent?.trim()).toBe(
        'Nobody',
      );
    });
  });
});
