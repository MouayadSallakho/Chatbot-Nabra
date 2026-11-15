import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { CustomerPage, CustomersApi } from '../customers-api';
import { CustomerList } from './customer-list';

/**
 * The customer directory.
 *
 * What matters here is that it is honest about who somebody is when nobody has
 * named them, that it never renders a label as markup, and that it asks for
 * nothing when the reader may not see it.
 */
describe('CustomerList', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const url = `/api/tenants/${tenantId}/customers`;

  const page: CustomerPage = {
    items: [
      {
        id: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        displayName: null,
        label: 'WhatsApp customer',
        labelCode: 'WhatsAppCustomer',
        shortCode: 'W4T5PQ',
        reference: '••••4821',
        channels: ['WhatsApp'],
        conversationCount: 3,
        lastActivityAt: '2026-09-22T10:00:00+00:00',
        createdAt: '2026-08-01T10:00:00+00:00',
      },
      {
        id: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        displayName: null,
        label: 'Anonymous visitor',
        labelCode: 'AnonymousVisitor',
        shortCode: '7KQ2MX',
        reference: null,
        channels: ['WebChat'],
        conversationCount: 1,
        lastActivityAt: null,
        createdAt: '2026-09-01T10:00:00+00:00',
      },
    ],
    page: 1,
    pageSize: 20,
    totalCount: 2,
  };

  let fixture: ComponentFixture<CustomerList>;
  let http: HttpTestingController;

  async function mount(...permissions: readonly string[]): Promise<void> {
    TestBed.configureTestingModule({
      imports: [CustomerList],
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

    fixture = TestBed.createComponent(CustomerList);

    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  async function answer(body: CustomerPage): Promise<void> {
    http.expectOne((request) => request.url === url).flush(body);
    await settle();
  }

  afterEach(() => http.verify());

  it('asks for nothing and explains itself without customers.view', async () => {
    await mount('conversations.view');

    // No request at all: one that was certain to be refused would only produce
    // an error to explain away.
    http.expectNone((request) => request.url === url);

    expect(text()).toContain('do not have access to customers');
  });

  it('does not reuse conversations.view for the directory', async () => {
    // Deliberate: being trusted to read what customers said is a different
    // decision from being handed a list of everybody a business serves.
    await mount('conversations.view');

    expect(text()).toContain('do not have access to customers');
  });

  it('reads the directory with customers.view', async () => {
    await mount('customers.view');
    await answer(page);

    expect(host().querySelectorAll('[data-testid^="customer-"]').length).toBe(2);

    // The server's code, worded as the inbox words it (BA-1), the code left to right.
    const labels = [...host().querySelectorAll('[data-testid="row-label"]')];

    // An unnamed WhatsApp customer reads its masked number, exactly as in the
    // inbox (39.1, note 18); a visitor its short code.
    expect(labels.map((label) => label.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      'WhatsApp customer ••••4821',
      'Visitor 7KQ2MX',
    ]);

    for (const label of labels) {
      expect(label.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
      expect(label.querySelector('bdi')?.getAttribute('translate')).toBe('no');
    }

    // Never the full number: the server sends only the masked one.
    expect(text()).not.toMatch(/\d{5,}/);
  });

  it('falls back to the short code when a read carries no masked number', async () => {
    await mount('customers.view');
    await answer({ ...page, items: [{ ...page.items[0], reference: null }] });

    expect(
      host().querySelector('[data-testid="row-label"]')?.textContent?.replace(/\s+/g, ' ').trim(),
    ).toBe('WhatsApp customer W4T5PQ');
  });

  it('leads each row with the inbox avatar: initials or the code, and the channel badge', async () => {
    await mount('customers.view');
    await answer({
      ...page,
      items: [
        { ...page.items[0], displayName: 'Rania Haddad', labelCode: 'Named' },
        page.items[1],
        { ...page.items[0], id: 'cccccccc-cccc-cccc-cccc-cccccccccccc' },
      ],
    });

    const labels = [...host().querySelectorAll('[data-testid="row-label"]')];
    const avatars = [
      ...host().querySelectorAll<HTMLElement>('[data-testid="customers"] app-avatar'),
    ];

    expect(labels[0].textContent?.trim()).toBe('Rania Haddad');
    expect(labels[0].querySelector('bdi')?.getAttribute('dir')).toBe('auto');

    expect(avatars.map((avatar) => avatar.querySelector('.avatar-letters')?.textContent)).toEqual([
      'RH',
      '7K',
      '48',
    ]);
    expect(
      avatars.map((avatar) => avatar.querySelector('.avatar-badge')?.getAttribute('data-channel')),
    ).toEqual(['WhatsApp', 'WebChat', 'WhatsApp']);

    // Decorative: the label beside it says all of it in words.
    expect(avatars.every((avatar) => avatar.getAttribute('aria-hidden') === 'true')).toBe(true);
  });

  it("draws the skeleton in the loaded rows' shape, then replaces it", async () => {
    await mount('customers.view');

    const skeleton = host().querySelector<HTMLElement>('[data-testid="customers-skeleton"]')!;

    expect(skeleton.getAttribute('aria-busy')).toBe('true');
    expect(skeleton.querySelectorAll('.data-list-head span').length).toBe(4);

    const rows = [...skeleton.querySelectorAll('.data-row-skeleton')];

    expect(rows.length).toBe(5);
    expect(rows.every((row) => row.getAttribute('aria-hidden') === 'true')).toBe(true);
    // The avatar's disc, the label's bar, a channel and the two end-aligned cells.
    expect(rows[0].querySelector('.customer-avatar-skeleton')).not.toBeNull();
    expect(rows[0].querySelectorAll('.skeleton').length).toBe(5);
    expect(rows[0].querySelectorAll('.data-cell-end').length).toBe(2);
    expect(text()).toContain('Loading customers');

    await answer(page);

    expect(host().querySelector('[data-testid="customers-skeleton"]')).toBeNull();
  });

  it('lays the list out as a calm panel of rows', async () => {
    await mount('customers.view');
    await answer(page);

    expect(host().querySelector('.page')?.classList.contains('page-calm')).toBe(true);
  });

  it('says a load failure in words and offers Try again', async () => {
    await mount('customers.view');
    http
      .expectOne((request) => request.url === url)
      .flush({ title: 'Down', code: 'something.down' }, { status: 503, statusText: 'Down' });
    await settle();

    expect(text()).toContain('Could not load customers');
    expect(text()).toContain('Try again');
    expect(text()).not.toContain('Down');
  });

  it('says a channel in words rather than by colour alone', async () => {
    await mount('customers.view');
    await answer(page);

    // Somebody who cannot tell the badges apart still reads which channels each
    // person uses.
    expect(text()).toContain('WhatsApp');
    expect(text()).toContain('Website');
  });

  it('says plainly when a customer has no activity yet', async () => {
    await mount('customers.view');
    await answer(page);

    expect(text()).toContain('No activity yet');
  });

  it('counts conversations in words rather than leaving a bare number', async () => {
    await mount('customers.view');
    await answer(page);

    expect(text()).toContain('3 conversations');
    expect(text()).toContain('1 conversation');
  });

  it('says nobody has been in touch rather than showing an empty list', async () => {
    await mount('customers.view');
    await answer({ items: [], page: 1, pageSize: 20, totalCount: 0 });

    expect(text()).toContain('Nobody has been in touch yet');

    // And says why, so nobody looks for a missing import button.
    expect(text()).toContain('Nothing is created by a page view');
  });

  it('renders a hostile label as text and never as markup', async () => {
    await mount('customers.view');

    await answer({
      ...page,
      items: [
        {
          ...page.items[0],
          displayName: '<script>alert(1)</script>',
          label: '<script>alert(1)</script>',
          labelCode: 'Named',
          shortCode: 'S7R1PT',
        },
      ],
    });

    // The text is there; the element is not.
    expect(text()).toContain('<script>alert(1)</script>');
    expect(host().querySelector('script')).toBeNull();
  });

  it('asks for one bounded page and never for everything', async () => {
    await mount('customers.view');

    const request = http.expectOne((candidate) => candidate.url === url);

    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('page')).toBe('1');

    request.flush(page);
    await settle();
  });

  it('offers no next page when there is nothing after this one', async () => {
    await mount('customers.view');
    await answer(page);

    // A single page shows no page controls at all; with more, Next is disabled
    // on the last one. Either way there is nothing to press.
    const next = [...host().querySelectorAll<HTMLButtonElement>('.pager button')].find((button) =>
      button.textContent?.includes('Next'),
    );

    expect(next === undefined || next.disabled).toBe(true);
  });
});
