import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { ToastService } from '@app/core/feedback/toast-service';
import { AiAgentApi } from '@app/features/ai-agents/ai-agent-api';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WhatsAppApi } from '../../whatsapp-api';
import { WhatsAppPage } from './whatsapp-page';

describe('WhatsAppPage', () => {
  const tenantId = 'aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa';
  const agentId = 'cccccccc-cccc-7ccc-8ccc-cccccccccccc';
  const url = `/api/tenants/${tenantId}/whatsapp/connections`;

  let fixture: ComponentFixture<WhatsAppPage>;
  let http: HttpTestingController;

  const connection = (status: string, hasCredential: boolean) => ({
    id: 'bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb',
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

  async function mount(connections: unknown[], ...permissions: string[]): Promise<void> {
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
    fixture = TestBed.createComponent(WhatsAppPage);
    fixture.detectChanges();
    await settle();

    if (!permissions.includes('whatsapp.view')) {
      return;
    }

    http.expectOne(url).flush(connections);
    await settle();

    if (permissions.includes('whatsapp.manage')) {
      http
        .expectOne((request) => request.url.includes('/ai-agents'))
        .flush({ items: [{ id: agentId, name: 'Concierge', status: 'Active' }], total: 1 });

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

  it('lists connected numbers and flags the ones with no token', async () => {
    await mount([connection('Draft', false)], 'whatsapp.view');

    expect(text()).toContain('+44 7700 900000');
    expect(text()).toContain('Draft');
    expect(text()).toContain('No access token');
  });

  it('says plainly when nothing is connected yet', async () => {
    await mount([], 'whatsapp.view');

    expect(text()).toContain('No numbers connected yet');
  });

  it('offers no way to connect a number to somebody who may only look', async () => {
    await mount([connection('Active', true)], 'whatsapp.view');

    expect(button('Connect number')).toBeUndefined();
  });

  it('refuses identifiers that are not digits before making a request', async () => {
    await mount([], 'whatsapp.view', 'whatsapp.manage');

    const select = host().querySelector<HTMLSelectElement>('select')!;
    select.value = agentId;
    select.dispatchEvent(new Event('change'));

    const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

    inputs[0].value = '111222333444555';
    inputs[0].dispatchEvent(new Event('input'));

    // A phone number pasted with a plus and spaces, which is the mistake this
    // catches before a round trip.
    inputs[1].value = '+44 7700 900000';
    inputs[1].dispatchEvent(new Event('input'));

    await settle();

    expect(button('Connect number')?.disabled).toBe(true);
    // Said beside the field that is wrong, not only by a disabled button.
    expect(inputs[0].getAttribute('aria-invalid')).toBe('false');
    expect(inputs[1].getAttribute('aria-invalid')).toBe('true');
    expect(inputs[1].getAttribute('aria-describedby')).toContain('phone-id-error');
    expect(host().querySelector('#phone-id-error')?.textContent).toContain('Use digits only');
    expect(host().querySelector('#waba-error')).toBeNull();
  });

  it('connects a number and re-reads the list', async () => {
    await mount([], 'whatsapp.view', 'whatsapp.manage');
    document.body.append(host());

    const select = host().querySelector<HTMLSelectElement>('select')!;
    select.value = agentId;
    select.dispatchEvent(new Event('change'));

    const inputs = [...host().querySelectorAll<HTMLInputElement>('input')];

    inputs[0].value = '111222333444555';
    inputs[0].dispatchEvent(new Event('input'));
    inputs[1].value = '100200300400500';
    inputs[1].dispatchEvent(new Event('input'));

    await settle();

    button('Connect number')!.click();
    await settle();

    const created = http.expectOne((request) => request.url === url && request.method === 'POST');

    expect(created.request.body).toEqual({
      aiAgentId: agentId,
      wabaId: '111222333444555',
      phoneNumberId: '100200300400500',
      displayPhoneNumber: null,
    });

    created.flush(connection('Draft', false), { headers: { ETag: '"v1"' } });
    await settle();

    http.expectOne(url).flush([connection('Draft', false)]);
    await settle();

    http
      .expectOne((request) => request.url.includes('/ai-agents'))
      .flush({ items: [{ id: agentId, name: 'Concierge', status: 'Active' }], total: 1 });

    await settle();

    expect(text()).toContain('+44 7700 900000');
    // Said, and focus is not left on a button the cleared form disables.
    expect(TestBed.inject(ToastService).toasts()[0]?.text).toContain('Number connected');
    expect(document.activeElement).toBe(host().querySelector('#connected-numbers'));

    host().remove();
  });

  it('explains a refusal rather than showing an empty list', async () => {
    await mount([], 'workspace.view');

    expect(text()).toContain('You do not have access to WhatsApp');
  });
});
