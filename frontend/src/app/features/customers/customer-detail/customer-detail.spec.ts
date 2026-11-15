import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { Customer, CustomersApi } from '../customers-api';
import { CustomerDetail } from './customer-detail';

/**
 * One customer's page.
 *
 * What matters here is that combining two records is never one click, that a
 * label is rendered as text, that a lost race keeps what somebody typed, and
 * that the conversation history is behind its own permission.
 */
describe('CustomerDetail', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const customerId = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
  const otherId = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

  const url = `/api/tenants/${tenantId}/customers/${customerId}`;
  const directoryUrl = `/api/tenants/${tenantId}/customers`;

  const customer: Customer = {
    id: customerId,
    displayName: null,
    label: 'WhatsApp customer',
    labelCode: 'WhatsAppCustomer',
    shortCode: 'W4T5PQ',
    reference: '••••0123',
    identities: [{ channel: 'WhatsApp', reference: '••••0123' }],
    channels: ['WhatsApp'],
    conversationCount: 2,
    lastActivityAt: '2026-09-22T10:00:00+00:00',
    createdAt: '2026-08-01T10:00:00+00:00',
    updatedAt: '2026-08-01T10:00:00+00:00',
    mergedIntoCustomerId: null,
    mergedAt: null,
    conversations: [
      {
        id: 'cccccccc-cccc-cccc-cccc-cccccccccccc',
        channel: 'WhatsApp',
        status: 'Open',
        handling: 'Ai',
        startedAt: '2026-09-22T09:00:00+00:00',
        lastActivityAt: '2026-09-22T10:00:00+00:00',
      },
    ],
    canReadConversations: true,
  };

  let fixture: ComponentFixture<CustomerDetail>;
  let http: HttpTestingController;

  async function mount(...permissions: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [CustomerDetail],
      providers: [
        {
          provide: PermissionApi,
          useValue: permissionApiGranting(...(permissions.length ? permissions : ['*'])),
        },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        CustomersApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(CustomerDetail);
    fixture.componentRef.setInput('customerId', customerId);

    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';
  const query = <T extends HTMLElement>(selector: string): T | null =>
    host().querySelector<T>(selector);

  async function answer(body: Customer = customer, etag = '"1"'): Promise<void> {
    http
      .expectOne((request) => request.url === url && request.method === 'GET')
      .flush(body, { headers: { ETag: etag } });

    await settle();
  }

  afterEach(() => http.verify());

  describe('authorization', () => {
    it('asks for nothing without customers.view', async () => {
      await mount('conversations.view');

      http.expectNone((request) => request.url === url);

      expect(text()).toContain('do not have access to customers');
    });

    it('reads it with customers.view and offers nothing to change', async () => {
      await mount('customers.view');
      await answer();

      expect(text()).toContain('WhatsApp customer');

      // Viewing is not naming, and it is certainly not combining.
      expect(query('[data-testid="save-name"]')).toBeNull();
      expect(query('[data-testid="open-merge"]')).toBeNull();
    });

    it('offers both operations with customers.manage', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      expect(query('[data-testid="save-name"]')).not.toBeNull();
      expect(query('[data-testid="open-merge"]')).not.toBeNull();
    });
  });

  describe('what it shows', () => {
    it('leads with the inbox avatar and the facts as tiles, in a calm panel', async () => {
      await mount('customers.view');
      await answer();

      const summary = query<HTMLElement>('[data-testid="customer-summary"]')!;
      const avatar = summary.querySelector('app-avatar')!;

      expect(query('.page')?.classList.contains('page-calm')).toBe(true);
      // The masked number's first digits, with the WhatsApp badge; decorative.
      expect(avatar.querySelector('.avatar-letters')?.textContent).toBe('01');
      expect(avatar.querySelector('.avatar-badge')?.getAttribute('data-channel')).toBe('WhatsApp');
      expect(avatar.getAttribute('aria-hidden')).toBe('true');
      expect(summary.querySelectorAll('.facts > div').length).toBe(4);
      expect(query('[data-testid="label"]')?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        'WhatsApp customer ••••0123',
      );
    });

    it("draws the skeleton in the loaded page's shape while it reads", async () => {
      await mount('customers.view');

      const skeleton = query<HTMLElement>('[data-testid="customer-skeleton"]')!;

      expect(skeleton.getAttribute('aria-busy')).toBe('true');
      expect(skeleton.querySelector('.customer-avatar-skeleton')).not.toBeNull();
      expect(skeleton.querySelectorAll('.customer-fact-skeleton').length).toBe(4);
      expect(skeleton.querySelectorAll('.customer-skeleton-card').length).toBe(2);

      await answer();

      expect(query('[data-testid="customer-skeleton"]')).toBeNull();
    });

    it('shows a masked identifier and says what it is', async () => {
      await mount('customers.view');
      await answer();

      expect(text()).toContain('••••0123');

      // Not a bare string of dots beside a badge: a reader using a screen reader
      // is told what the value is.
      expect(
        host().querySelector('[data-testid="identities"] .visually-hidden')?.textContent,
      ).toContain('Masked identifier');
    });

    it('says a website visitor has no lasting identifier rather than leaving a gap', async () => {
      await mount('customers.view');

      await answer({
        ...customer,
        identities: [],
        channels: ['WebChat'],
        label: 'Anonymous visitor',
        labelCode: 'AnonymousVisitor',
        shortCode: '7KQ2MX',
      });

      expect(text()).toContain('No lasting identifier');
      expect(text()).toContain('does not try to recognise them across visits');
    });

    it('withholds the history without conversations.view and says why', async () => {
      await mount('customers.view');

      await answer({ ...customer, conversations: [], canReadConversations: false });

      // Not an empty list, which would read as "this customer has never
      // written". The count is still shown, because knowing there are two is not
      // being able to read them.
      expect(query('[data-testid="conversations-denied"]')).not.toBeNull();
      expect(text()).toContain('2 conversations');
    });

    it('lists the history with conversations.view', async () => {
      await mount('customers.view', 'conversations.view');
      await answer();

      expect(host().querySelectorAll('[data-testid="conversations"] a').length).toBe(1);
    });

    it('renders a hostile label as text and never as markup', async () => {
      await mount('customers.view');

      await answer({
        ...customer,
        displayName: '<script>alert(1)</script>',
        label: '<script>alert(1)</script>',
        labelCode: 'Named',
        shortCode: 'S7R1PT',
      });

      expect(text()).toContain('<script>alert(1)</script>');
      expect(host().querySelector('script')).toBeNull();
    });

    it('tells somebody following an old link where the history went', async () => {
      await mount('customers.view');

      await answer({
        ...customer,
        mergedIntoCustomerId: otherId,
        mergedAt: '2026-09-23T10:00:00+00:00',
      });

      expect(query('[data-testid="merged-notice"]')).not.toBeNull();
      expect(text()).toContain('combined with another customer');

      // And nothing is offered on a tombstone.
      expect(query('[data-testid="save-name"]')).toBeNull();
      expect(query('[data-testid="open-merge"]')).toBeNull();
    });
  });

  describe('naming', () => {
    it('sends the version it read and keeps the new one', async () => {
      await mount('customers.view', 'customers.manage');
      await answer(customer, '"7"');

      const input = query<HTMLInputElement>('[data-testid="name-input"]')!;

      input.value = 'Dana Okafor';
      input.dispatchEvent(new Event('input'));

      await settle();

      query<HTMLButtonElement>('[data-testid="save-name"]')!.click();

      const request = http.expectOne(
        (candidate) => candidate.url === `${url}/display-name` && candidate.method === 'PUT',
      );

      expect(request.request.headers.get('If-Match')).toBe('"7"');
      expect(request.request.body).toEqual({ displayName: 'Dana Okafor' });

      request.flush(
        {
          ...customer,
          displayName: 'Dana Okafor',
          label: 'Dana Okafor',
          labelCode: 'Named',
          shortCode: 'D4N4OK',
        },
        {
          headers: { ETag: '"8"' },
        },
      );

      await settle();

      expect(text()).toContain('Saved.');
    });

    it('keeps what somebody typed when they lose a race, and never retries', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      const input = query<HTMLInputElement>('[data-testid="name-input"]')!;

      input.value = 'Dana Okafor';
      input.dispatchEvent(new Event('input'));

      await settle();

      query<HTMLButtonElement>('[data-testid="save-name"]')!.click();

      http
        .expectOne((candidate) => candidate.url === `${url}/display-name`)
        .flush(
          { title: 'This customer changed' },
          { status: 412, statusText: 'Precondition Failed' },
        );

      await settle();

      // Said plainly, with the next step.
      expect(text()).toContain('Somebody else changed this customer');
      expect(text()).toContain('nothing you typed has been lost');

      // And it is still in the box: retyping it would be the application's
      // mistake, not the reader's.
      expect(query<HTMLInputElement>('[data-testid="name-input"]')!.value).toBe('Dana Okafor');

      // No automatic retry. The next request is only the one a person asks for.
      http.expectNone((candidate) => candidate.url === `${url}/display-name`);
    });

    it('clears a name by sending null rather than an empty string', async () => {
      await mount('customers.view', 'customers.manage');

      await answer({
        ...customer,
        displayName: 'Dana Okafor',
        label: 'Dana Okafor',
        labelCode: 'Named',
        shortCode: 'D4N4OK',
      });

      const input = query<HTMLInputElement>('[data-testid="name-input"]')!;

      input.value = '   ';
      input.dispatchEvent(new Event('input'));

      await settle();

      query<HTMLButtonElement>('[data-testid="save-name"]')!.click();

      const request = http.expectOne((candidate) => candidate.url === `${url}/display-name`);

      expect(request.request.body).toEqual({ displayName: null });

      request.flush(customer, { headers: { ETag: '"2"' } });

      await settle();
    });
  });

  describe('combining two customers', () => {
    it('is never one click', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      // Opening the step is not performing it, and nothing is aimed anywhere yet.
      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: otherId,
              displayName: null,
              label: 'Anonymous visitor',
              labelCode: 'AnonymousVisitor',
              shortCode: '7KQ2MX',
              channels: ['WebChat'],
              conversationCount: 1,
              lastActivityAt: null,
              createdAt: '2026-09-01T10:00:00+00:00',
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });

      await settle();

      const confirm = query<HTMLButtonElement>('[data-testid="confirm-merge"]')!;

      expect(confirm.disabled).toBe(true);
      expect(query('[data-testid="merge-confirmation"]')).toBeNull();
    });

    it('names both records and says what it does before it will run', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: otherId,
              displayName: null,
              label: 'Anonymous visitor',
              labelCode: 'AnonymousVisitor',
              shortCode: '7KQ2MX',
              channels: ['WebChat'],
              conversationCount: 1,
              lastActivityAt: null,
              createdAt: '2026-09-01T10:00:00+00:00',
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });

      await settle();

      query<HTMLInputElement>(`[data-testid="candidate-${otherId}"]`)!.click();

      await settle();

      // Both named by the server's code, as the inbox names them (BA-1): the
      // WhatsApp customer by its masked number (39.1), so its short code follows
      // the facts, once; the visitor by its short code, which is not repeated.
      const confirmation = query<HTMLElement>('[data-testid="merge-confirmation"]')!;
      const said = confirmation.textContent ?? '';

      expect(said).toContain('WhatsApp customer ••••0123');
      expect(said).toContain('· \u2066W4T5PQ\u2069)');
      expect(said).toContain('Visitor 7KQ2MX');
      expect(said.split('7KQ2MX').length - 1).toBe(1);
      expect(said.split('W4T5PQ').length - 1).toBe(1);
      expect(said).not.toContain('ref.');
      expect(text()).toContain('combines their channel identities and conversation history');

      const candidate = query<HTMLElement>('[data-testid="candidates"] .candidate-name')!;

      expect(candidate.textContent?.replace(/\s+/g, ' ').trim()).toBe('Visitor 7KQ2MX');
      expect(candidate.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');

      // One code per candidate (note 15): the label carries it, so the facts do not.
      const facts = query<HTMLElement>('[data-testid="candidate-facts"]')!;

      expect(facts.textContent).not.toContain('7KQ2MX');
      expect(facts.textContent).not.toContain('ref.');
      expect(facts.querySelector('bdi')).toBeNull();

      // It asks the reader to assert it, rather than asserting it for them.
      expect(text()).toContain('Confirm that these records belong to the same customer');
      expect(text()).toContain('cannot be undone');

      expect(query<HTMLButtonElement>('[data-testid="confirm-merge"]')!.disabled).toBe(false);
    });

    it('tells two unnamed customers apart, because the label cannot', async () => {
      // Found in the browser: two website visitors both render as "Anonymous
      // visitor", so the picker offered a choice between two identical rows for
      // an operation that cannot be undone.
      await mount('customers.view', 'customers.manage');

      await answer({
        ...customer,
        channels: ['WebChat'],
        label: 'Anonymous visitor',
        labelCode: 'AnonymousVisitor',
        shortCode: '7KQ2MX',
      });

      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: otherId,
              displayName: null,
              label: 'Anonymous visitor',
              labelCode: 'AnonymousVisitor',
              shortCode: '7KQ2MX',
              channels: ['WebChat'],
              conversationCount: 1,
              lastActivityAt: null,
              createdAt: '2026-09-01T10:00:00+00:00',
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });

      await settle();

      // Each row says which channel and when the workspace first heard from
      // them -- the only distinguishing facts the platform actually holds.
      expect(text()).toContain('Website chat · first seen');

      query<HTMLInputElement>(`[data-testid="candidate-${otherId}"]`)!.click();

      await settle();

      const confirmation = query('[data-testid="merge-confirmation"]')!.textContent ?? '';

      // Both sides of the confirmation are distinguishable, so somebody cannot
      // combine the wrong pair by reading two identical names. The dates are
      // formatted for the reader's locale, so they are compared as the component
      // would render them rather than as a hard-coded English string.
      const on = (iso: string): string =>
        new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' });

      expect(confirmation).toContain(`first seen ${on('2026-08-01T10:00:00+00:00')}`);
      expect(confirmation).toContain(`first seen ${on('2026-09-01T10:00:00+00:00')}`);
      expect(on('2026-08-01T10:00:00+00:00')).not.toEqual(on('2026-09-01T10:00:00+00:00'));
    });

    it('tells apart two visitors first seen in the same minute, by one code each', async () => {
      // Found in Phase 33.5: seeded visitors who arrived together still read
      // identically by channel and date. Since 39.6a (note 15) the short code in
      // each label tells them apart; it is read from the same random end of the
      // id the old "ref." was, with more of it, so nothing is lost.
      const sameMoment = '2026-08-01T10:00:00+00:00';

      await mount('customers.view', 'customers.manage');
      await answer({
        ...customer,
        channels: ['WebChat'],
        label: 'Anonymous visitor',
        labelCode: 'AnonymousVisitor',
        shortCode: '7KQ2MX',
        createdAt: sameMoment,
      });

      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: otherId,
              displayName: null,
              label: 'Anonymous visitor',
              labelCode: 'AnonymousVisitor',
              shortCode: 'P3Z9QA',
              channels: ['WebChat'],
              conversationCount: 1,
              lastActivityAt: null,
              createdAt: sameMoment,
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });

      await settle();

      query<HTMLInputElement>(`[data-testid="candidate-${otherId}"]`)!.click();
      await settle();

      const confirmation = query('[data-testid="merge-confirmation"]')!.textContent ?? '';

      expect(confirmation).toContain('Visitor 7KQ2MX');
      expect(confirmation).toContain('Visitor P3Z9QA');
      expect(confirmation).not.toContain('ref.');
      expect(confirmation.split('7KQ2MX').length - 1).toBe(1);
      expect(confirmation.split('P3Z9QA').length - 1).toBe(1);
    });

    it('gives a named candidate its short code once, after the facts', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();
      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: otherId,
              displayName: 'Rania Haddad',
              label: 'Rania Haddad',
              labelCode: 'Named',
              shortCode: 'R4N1AH',
              reference: null,
              channels: ['WebChat'],
              conversationCount: 1,
              lastActivityAt: null,
              createdAt: '2026-09-01T10:00:00+00:00',
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });
      await settle();

      const facts = query<HTMLElement>('[data-testid="candidate-facts"]')!;
      const code = facts.querySelector('bdi')!;

      expect(code.textContent).toBe('R4N1AH');
      expect(code.getAttribute('dir')).toBe('ltr');
      expect(code.getAttribute('translate')).toBe('no');
      expect(text().split('R4N1AH').length - 1).toBe(1);
    });

    async function pickOther(): Promise<void> {
      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: otherId,
              displayName: null,
              label: 'Anonymous visitor',
              labelCode: 'AnonymousVisitor',
              shortCode: '7KQ2MX',
              channels: ['WebChat'],
              conversationCount: 1,
              lastActivityAt: null,
              createdAt: '2026-09-01T10:00:00+00:00',
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });
      await settle();
      await new Promise((resolve) => setTimeout(resolve));
      await settle();
    }

    it('keeps focus with the reader through a merge', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();
      document.body.append(host());

      await pickOther();

      // The opening button was replaced by the picker.
      expect(document.activeElement).toBe(query(`[data-testid="candidate-${otherId}"]`));

      query<HTMLInputElement>(`[data-testid="candidate-${otherId}"]`)!.click();
      await settle();
      query<HTMLButtonElement>('[data-testid="confirm-merge"]')!.click();
      await settle();

      http
        .expectOne((request) => request.url === `${url}/merge` && request.method === 'POST')
        .flush({ sourceCustomerId: customerId, targetCustomerId: otherId });
      await settle();
      await new Promise((resolve) => setTimeout(resolve));
      await answer({
        ...customer,
        channels: [],
        identities: [],
        conversations: [],
        conversationCount: 0,
        mergedIntoCustomerId: otherId,
        mergedAt: '2026-09-26T10:00:00+00:00',
      });
      await new Promise((resolve) => setTimeout(resolve));
      await settle();

      // This page is now the tombstone, and focus is on what it says.
      expect(document.activeElement).toBe(query('[data-testid="merged-notice"]'));

      host().remove();
    });

    it('puts focus on a refused merge, which changes nothing', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();
      document.body.append(host());

      await pickOther();
      query<HTMLInputElement>(`[data-testid="candidate-${otherId}"]`)!.click();
      await settle();
      query<HTMLButtonElement>('[data-testid="confirm-merge"]')!.click();
      await settle();

      http
        .expectOne((request) => request.url === `${url}/merge` && request.method === 'POST')
        .flush(
          { title: 'Precondition failed', status: 412 },
          { status: 412, statusText: 'Precondition Failed' },
        );
      await settle();
      await new Promise((resolve) => setTimeout(resolve));
      await settle();

      expect(query('[data-testid="merged-notice"]')).toBeNull();
      expect(document.activeElement).toBe(query('[data-testid="merge-failure"]'));

      host().remove();
    });

    it('never offers this customer as its own target', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      http
        .expectOne((candidate) => candidate.url === directoryUrl)
        .flush({
          items: [
            {
              id: customerId,
              displayName: null,
              label: 'WhatsApp customer',
              labelCode: 'WhatsAppCustomer',
              shortCode: 'W4T5PQ',
              channels: ['WhatsApp'],
              conversationCount: 2,
              lastActivityAt: null,
              createdAt: '2026-08-01T10:00:00+00:00',
            },
          ],
          page: 1,
          pageSize: 20,
          totalCount: 1,
        });

      await settle();

      expect(query(`[data-testid="candidate-${customerId}"]`)).toBeNull();
      expect(text()).toContain('nobody else in this workspace to combine with');
    });

    it('asks for a bounded list of candidates, never for everybody', async () => {
      await mount('customers.view', 'customers.manage');
      await answer();

      query<HTMLButtonElement>('[data-testid="open-merge"]')!.click();

      const request = http.expectOne((candidate) => candidate.url === directoryUrl);

      expect(request.request.params.get('pageSize')).toBe('20');

      request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });

      await settle();
    });
  });
});
