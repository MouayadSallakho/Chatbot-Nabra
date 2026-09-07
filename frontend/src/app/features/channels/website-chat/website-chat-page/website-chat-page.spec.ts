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
import { WebChatAdminApi } from '../../website-chat-api';
import { WebsiteChatPage } from './website-chat-page';

describe('WebsiteChatPage', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const endpointsUrl = `/api/tenants/${tenantId}/website-chat`;
  const agentsUrl = `/api/tenants/${tenantId}/ai-agents?page=1&pageSize=100`;

  let fixture: ComponentFixture<WebsiteChatPage>;
  let http: HttpTestingController;

  const agent = (id: string, name: string, status = 'Active') => ({
    id,
    tenantId,
    name,
    status,
    createdAt: '2026-09-07T10:00:00Z',
    updatedAt: '2026-09-07T10:00:00Z',
  });

  const endpoint = (id: string, agentId: string, agentName: string, status = 'Active') => ({
    id,
    aiAgentId: agentId,
    aiAgentName: agentName,
    aiAgentStatus: 'Active',
    publicKey: 'aaaaaaaaaaaaaaaaaaaaaa',
    status,
    createdAt: '2026-09-07T10:00:00Z',
    updatedAt: '2026-09-07T10:00:00Z',
  });

  async function mount(...permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        WebChatAdminApi,
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
    fixture = TestBed.createComponent(WebsiteChatPage);
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
  const text = (): string => host().textContent ?? '';

  const select = (): HTMLSelectElement | null => host().querySelector('select');

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  afterEach(() => http.verify());

  it('lists the workspace’s chat links', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(endpointsUrl).flush([endpoint('e1', 'a1', 'Concierge')]);
    await settle();

    http
      .expectOne(agentsUrl)
      .flush({ items: [agent('a1', 'Concierge')], page: 1, pageSize: 100, totalCount: 1 });
    await settle();

    expect(text()).toContain('Concierge');
    expect(text()).toContain('Active');
  });

  it('says so plainly when there are no chat links yet', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(endpointsUrl).flush([]);
    await settle();

    http.expectOne(agentsUrl).flush({ items: [], page: 1, pageSize: 100, totalCount: 0 });
    await settle();

    expect(text()).toContain('No chat links yet');
  });

  it('creates a link for the chosen agent and reloads', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(endpointsUrl).flush([]);
    await settle();

    http.expectOne(agentsUrl).flush({
      items: [agent('a1', 'Concierge')],
      page: 1,
      pageSize: 100,
      totalCount: 1,
    });
    await settle();

    const picker = select()!;

    picker.value = 'a1';
    picker.dispatchEvent(new Event('change'));
    await settle();

    button('Create chat link')!.click();
    await settle();

    const created = http.expectOne(endpointsUrl);

    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual({ aiAgentId: 'a1' });

    created.flush(endpoint('e1', 'a1', 'Concierge', 'Draft'), { headers: { ETag: '"v1"' } });
    await settle();

    http.expectOne(endpointsUrl).flush([endpoint('e1', 'a1', 'Concierge', 'Draft')]);
    await settle();

    http.expectOne(agentsUrl).flush({
      items: [agent('a1', 'Concierge')],
      page: 1,
      pageSize: 100,
      totalCount: 1,
    });
    await settle();

    expect(text()).toContain('Draft');
  });

  it('does not offer an agent that already has a link', async () => {
    // A second link for the same agent is refused by the server, so offering it
    // would be offering a failure.
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(endpointsUrl).flush([endpoint('e1', 'a1', 'Concierge')]);
    await settle();

    http.expectOne(agentsUrl).flush({
      items: [agent('a1', 'Concierge'), agent('a2', 'Night desk')],
      page: 1,
      pageSize: 100,
      totalCount: 2,
    });
    await settle();

    const options = [...(select()?.querySelectorAll('option') ?? [])].map(
      (option) => option.textContent?.trim() ?? '',
    );

    expect(options.some((option) => option.includes('Night desk'))).toBe(true);
    expect(options.some((option) => option.includes('Concierge'))).toBe(false);
  });

  it('shows the list but no create form to somebody who may only read', async () => {
    // And it does not ask for the agent list, which would be a 403 in their
    // console for no reason.
    await mount('website-chat.view');

    http.expectOne(endpointsUrl).flush([endpoint('e1', 'a1', 'Concierge')]);
    await settle();

    expect(text()).toContain('Concierge');
    expect(select()).toBeNull();
    expect(button('Create chat link')).toBeUndefined();
  });

  it('explains a refusal to somebody with no channel permission', async () => {
    await mount();
    await settle();

    expect(text()).toContain('do not have access to website chat');
  });

  it('offers a retry when the list could not be read', async () => {
    await mount('website-chat.view');

    http
      .expectOne(endpointsUrl)
      .flush(
        { title: 'Server error', detail: 'Something went wrong.' },
        { status: 500, statusText: 'Server Error' },
      );
    await settle();

    expect(text()).toContain('Could not load website chat');
    expect(button('Try again')).toBeTruthy();
  });
});
