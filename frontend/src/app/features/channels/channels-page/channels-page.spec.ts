import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WebChatAdminApi } from '../website-chat-api';
import { WhatsAppApi } from '../whatsapp-api';
import { ChannelsPage } from './channels-page';

describe('ChannelsPage', () => {
  const tenantId = 'aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa';
  const webChatUrl = `/api/tenants/${tenantId}/website-chat`;
  const whatsAppUrl = `/api/tenants/${tenantId}/whatsapp/connections`;

  let fixture: ComponentFixture<ChannelsPage>;
  let http: HttpTestingController;

  async function mount(...permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        WebChatAdminApi,
        WhatsAppApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ChannelsPage);
    fixture.detectChanges();
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const card = (name: string): HTMLElement =>
    host().querySelector<HTMLElement>(`[data-testid="channel-${name}"]`)!;

  /** The figures on a card, as label: value pairs. */
  const facts = (name: string): Record<string, string> =>
    Object.fromEntries(
      [...card(name).querySelectorAll('dl > div')].map((pair) => [
        pair.querySelector('dt')?.textContent?.trim() ?? '',
        pair.querySelector('dd')?.textContent?.trim() ?? '',
      ]),
    );

  afterEach(() => http.verify());

  it('counts each built channel from its own list', async () => {
    await mount('website-chat.view', 'whatsapp.view');

    http.expectOne(webChatUrl).flush([
      { id: '1', status: 'Active' },
      { id: '2', status: 'Draft' },
      { id: '3', status: 'Disabled' },
    ]);
    http.expectOne(whatsAppUrl).flush([
      { id: 'w1', status: 'Draft', hasCredential: false },
      { id: 'w2', status: 'Active', hasCredential: true },
    ]);
    await settle();

    expect(facts('website-chat')).toEqual({ 'Chat links': '3', Published: '1' });
    expect(facts('whatsapp')).toEqual({ Numbers: '2', 'In service': '1' });

    const whatsApp = card('whatsapp');

    expect(whatsApp.textContent).toContain('Without an access token: 1');
  });

  it('says "Not checked" for a list that failed, never zero', async () => {
    await mount('website-chat.view', 'whatsapp.view');

    http.expectOne(webChatUrl).flush({}, { status: 500, statusText: 'Server Error' });
    http.expectOne(whatsAppUrl).flush([]);
    await settle();

    expect(card('website-chat').textContent).toContain('Not checked');
    expect(card('website-chat').querySelector('dl')).toBeNull();
    expect(facts('whatsapp')).toEqual({ Numbers: '0', 'In service': '0' });
  });

  it('reads only the channels this member may see', async () => {
    await mount('whatsapp.view');

    // No website chat request at all: verify() would fail on one.
    http.expectOne(whatsAppUrl).flush([]);
    await settle();

    expect(card('website-chat').textContent).toContain('You do not have access to website chat.');
    expect(card('website-chat').querySelector('a')).toBeNull();
    expect(card('whatsapp').querySelector('a')?.getAttribute('href')).toBe('/channels/whatsapp');
  });

  it('lists the channels that are not built with no button and no link', async () => {
    await mount('website-chat.view');

    http.expectOne(webChatUrl).flush([]);
    await settle();

    for (const name of ['instagram', 'messenger']) {
      const row = card(name);

      expect(row.textContent).toContain('Not available yet');
      expect(row.querySelector('a, button')).toBeNull();
    }
  });

  it('explains a missing permission instead of showing an empty page', async () => {
    await mount();

    expect(host().textContent).toContain('You do not have access to channels');
  });
});
