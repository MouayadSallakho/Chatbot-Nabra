import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { AiAgentApi } from '@app/features/ai-agents/ai-agent-api';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WhatsAppApi } from '../../whatsapp-api';
import { WhatsAppDetail } from './whatsapp-detail';

describe('WhatsAppDetail', () => {
  const tenantId = 'aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa';
  const connectionId = 'bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb';
  const agentId = 'cccccccc-cccc-7ccc-8ccc-cccccccccccc';

  /** Agents listed before the connection's own, for the selection test. */
  let agentsBefore: Array<{ id: string; name: string; status: string }> = [];
  const url = `/api/tenants/${tenantId}/whatsapp/connections/${connectionId}`;

  /** Invented, like every identifier in these suites. */
  const fakeToken = 'fake-local-test-token';

  let fixture: ComponentFixture<WhatsAppDetail>;
  let http: HttpTestingController;

  const connection = (status: string, hasCredential: boolean) => ({
    id: connectionId,
    aiAgentId: agentId,
    aiAgentName: 'Concierge',
    wabaId: '111222333444555',
    phoneNumberId: '100200300400500',
    displayPhoneNumber: '+44 7700 900000',
    status,
    hasCredential,
    createdAt: '2026-09-14T10:00:00Z',
    updatedAt: '2026-09-14T10:00:00Z',
  });

  async function mount(
    status = 'Draft',
    hasCredential = false,
    ...permissions: string[]
  ): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        WhatsAppApi,
        AiAgentApi,
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
    fixture = TestBed.createComponent(WhatsAppDetail);
    fixture.componentRef.setInput('connectionId', connectionId);
    fixture.detectChanges();
    await settle();

    http.expectOne(url).flush(connection(status, hasCredential), {
      headers: { ETag: '"v1"' },
    });

    await settle();

    if (permissions.includes('whatsapp.manage')) {
      http
        .expectOne((request) => request.url.includes('/ai-agents'))
        .flush({
          items: agentsBefore.concat([{ id: agentId, name: 'Concierge', status: 'Active' }]),
          total: 1 + agentsBefore.length,
        });

      await settle();
    }
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const text = (): string => host().textContent ?? '';

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      (candidate.textContent ?? '').includes(label),
    ) as HTMLButtonElement | undefined;

  afterEach(() => http?.verify());

  it('says whether a credential is configured, and never shows one', async () => {
    await mount('Draft', false, 'whatsapp.view', 'whatsapp.manage');

    expect(text()).toContain('Access token: not configured');

    // <strong>The central claim of the screen.</strong> Nothing renders a token,
    // because nothing has one to render.
    expect(text()).not.toContain(fakeToken);
  });

  it('will not offer to put a connection into service without a credential', async () => {
    await mount('Draft', false, 'whatsapp.view', 'whatsapp.manage');

    expect(button('Put into service')?.disabled).toBe(true);
    expect(text()).toContain('Add an access token before putting this number into service');
  });

  it('sends the token once, clears it, and shows it as configured', async () => {
    await mount('Draft', false, 'whatsapp.view', 'whatsapp.manage');

    const input = host().querySelector<HTMLInputElement>('input[type="password"]')!;

    input.value = fakeToken;
    input.dispatchEvent(new Event('input'));
    await settle();

    button('Save access token')!.click();
    await settle();

    const request = http.expectOne(`${url}/credential`);

    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ accessToken: fakeToken });

    // Conditional, like every other write on this screen.
    expect(request.request.headers.get('If-Match')).toBe('"v1"');

    request.flush(connection('Draft', true), { headers: { ETag: '"v2"' } });
    await settle();

    expect(text()).toContain('Access token: configured');
    expect(text()).toContain('cannot be displayed again');

    // Cleared, so it does not sit in the field or in memory after being stored.
    expect(host().querySelector<HTMLInputElement>('input[type="password"]')!.value).toBe('');
  });

  it('keeps what was typed when somebody else saved first', async () => {
    await mount('Draft', true, 'whatsapp.view', 'whatsapp.manage');

    const numberInput = [...host().querySelectorAll<HTMLInputElement>('input[type="text"]')][1];

    numberInput.value = '999888777666555';
    numberInput.dispatchEvent(new Event('input'));
    await settle();

    button('Save settings')!.click();
    await settle();

    http
      .expectOne(url)
      .flush(
        { title: 'Precondition failed', status: 412 },
        { status: 412, statusText: 'Precondition Failed' },
      );

    await settle();

    // The edit is still on screen: losing a race means somebody else changed the
    // connection, not that this person meant something else.
    expect([...host().querySelectorAll<HTMLInputElement>('input[type="text"]')][1].value).toBe(
      '999888777666555',
    );

    expect(text()).toContain('updated somewhere else');
  });

  it('says beside an identifier what is wrong with it', async () => {
    await mount('Draft', true, 'whatsapp.view', 'whatsapp.manage');

    const numberInput = [...host().querySelectorAll<HTMLInputElement>('input[type="text"]')][1];

    numberInput.value = '9000 45x';
    numberInput.dispatchEvent(new Event('input'));
    await settle();

    expect(button('Save settings')?.disabled).toBe(true);
    expect(numberInput.getAttribute('aria-invalid')).toBe('true');
    expect(host().querySelector('#detail-phone-id-error')?.textContent).toContain(
      'Use digits only',
    );

    numberInput.value = '';
    numberInput.dispatchEvent(new Event('input'));
    await settle();

    expect(host().querySelector('#detail-phone-id-error')?.textContent).toContain('Required');
  });

  it('will not let a live connection be reconfigured', async () => {
    await mount('Active', true, 'whatsapp.view', 'whatsapp.manage');

    expect(button('Save settings')?.disabled).toBe(true);
    expect(button('Take out of service')).toBeDefined();
  });

  it('offers nothing to change to somebody who may only look', async () => {
    await mount('Active', true, 'whatsapp.view');

    expect(button('Save settings')).toBeUndefined();
    expect(button('Save access token')).toBeUndefined();
    expect(button('Take out of service')).toBeUndefined();
  });

  it('explains a refusal rather than showing an empty screen', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        WhatsAppApi,
        AiAgentApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting('workspace.view') },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WhatsAppDetail);
    fixture.componentRef.setInput('connectionId', connectionId);
    fixture.detectChanges();
    await settle();

    expect(text()).toContain('You do not have access to WhatsApp');
  });

  it('marks where the number stands in its lifecycle, and says what that means', async () => {
    await mount('Disabled', true, 'whatsapp.view');

    const current = host().querySelector('.lifecycle-step[aria-current="step"]');

    expect(current?.textContent?.trim()).toBe('Disabled');
    expect(host().querySelectorAll('.lifecycle-step').length).toBe(3);
    expect(text()).toContain('Out of service. Messages are not answered');
  });

  it('shows the agent that really answers the number, even though the agents arrive later', async () => {
    // The connection's agent is not the first in the list: before the fix the
    // browser showed the first option instead.
    agentsBefore = [
      { id: 'dddddddd-dddd-7ddd-8ddd-dddddddddddd', name: 'Returns Desk', status: 'Active' },
    ];
    await mount('Draft', true, 'whatsapp.view', 'whatsapp.manage');
    agentsBefore = [];

    const select = host().querySelector<HTMLSelectElement>('#detail-agent')!;

    expect(select.value).toBe(agentId);
    expect(select.selectedOptions[0]?.textContent).toContain('Concierge');
  });
});
