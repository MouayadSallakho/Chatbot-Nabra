import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WebChatAdminApi } from '../../website-chat-api';
import { WebsiteChatDetail } from './website-chat-detail';

describe('WebsiteChatDetail', () => {
  const tenantId = 'aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa';
  const endpointId = 'bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb';
  const url = `/api/tenants/${tenantId}/website-chat/${endpointId}`;

  let fixture: ComponentFixture<WebsiteChatDetail>;
  let http: HttpTestingController;

  const endpoint = (status: string, agentStatus = 'Active') => ({
    id: endpointId,
    aiAgentId: 'cccccccc-cccc-7ccc-8ccc-cccccccccccc',
    aiAgentName: 'Concierge',
    aiAgentStatus: agentStatus,
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
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting(...permissions) },
      ],
    });

    // The workspace has to be resolved before the component reads it, exactly as
    // the shell resolves it before rendering a feature page.
    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WebsiteChatDetail);
    fixture.componentRef.setInput('webChatEndpointId', endpointId);
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

  /**
   * The widget section's own read.
   *
   * It is a child of this page with its own request and its own version, so every
   * render of the page makes exactly one of these. Flushing it here keeps these
   * tests about the page while still proving the section is mounted.
   */
  async function flushWidget(etag = '"v1"'): Promise<void> {
    http.expectOne(`${url}/widget`).flush(
      {
        id: endpointId,
        publicKey: 'aaaaaaaaaaaaaaaaaaaaaa',
        status: 'Active',
        widget: {
          greeting: null,
          accentColor: '#2563eb',
          launcherPosition: 'Right',
          theme: 'System',
        },
        allowedOrigins: [],
        maxAllowedOrigins: 20,
        installSnippet: '<script async src="https://omniai.test/widget/v1.js"></script>',
        loaderUrl: 'https://omniai.test/widget/v1.js',
      },
      { headers: { ETag: etag } },
    );
    await settle();
  }

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  afterEach(() => http.verify());

  it('shows the hosted chat link so it can be copied', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(endpoint('Active'), { headers: { ETag: '"v1"' } });
    await settle();
    await flushWidget();

    expect(text()).toContain(`/chat/aaaaaaaaaaaaaaaaaaaaaa`);
    expect(button('Copy chat link')).toBeTruthy();
  });

  it('publishes with the version it read, and adopts the new one', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(endpoint('Draft'), { headers: { ETag: '"v1"' } });
    await settle();
    await flushWidget();

    button('Publish chat')!.click();
    await settle();

    const request = http.expectOne(`${url}/activate`);

    expect(request.request.headers.get('If-Match')).toBe('"v1"');

    request.flush(endpoint('Active'), { headers: { ETag: '"v2"' } });
    await settle();

    // Publishing changed the same aggregate the widget section holds a version
    // of, so it re-reads. It only does that because nothing was typed there.
    await flushWidget('"v2"');

    expect(text()).toContain('Disable chat');

    // The new tag is the one the next change presents.
    button('Disable chat')!.click();
    await settle();

    const disable = http.expectOne(`${url}/disable`);

    expect(disable.request.headers.get('If-Match')).toBe('"v2"');

    disable.flush(endpoint('Draft'), { headers: { ETag: '"v3"' } });
    await settle();
    await flushWidget('"v3"');
  });

  it('offers a deliberate reload after a stale write, and retries nothing', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(endpoint('Draft'), { headers: { ETag: '"v1"' } });
    await settle();
    await flushWidget();

    button('Publish chat')!.click();
    await settle();

    http
      .expectOne(`${url}/activate`)
      .flush(
        { title: 'Precondition failed', detail: 'Changed elsewhere.' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await settle();

    expect(text()).toContain('updated elsewhere');
    expect(button('Reload latest')).toBeTruthy();

    // Nothing was retried: verify() in afterEach would fail on a second request.
  });

  it('does not tear down the widget section when it re-reads after a change', async () => {
    // Found in the browser: publishing re-read this endpoint, and flipping to the
    // loading branch destroyed and re-created the widget section underneath the
    // reader -- taking its "Saved." confirmation, and any part-typed allowlist,
    // with it. A refresh of something already on screen is not a load.
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(endpoint('Draft'), { headers: { ETag: '"v1"' } });
    await settle();
    await flushWidget();

    const section = host().querySelector('app-website-chat-widget');

    expect(section).not.toBeNull();

    button('Publish chat')!.click();
    await settle();

    http.expectOne(`${url}/activate`).flush(endpoint('Active'), { headers: { ETag: '"v2"' } });
    await settle();
    await flushWidget('"v2"');

    expect(host().querySelector('.skeleton')).toBeNull();
    expect(host().querySelector('app-website-chat-widget')).toBe(section);
  });

  it('warns when publishing an endpoint whose agent is not active', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(endpoint('Draft', 'Draft'), { headers: { ETag: '"v1"' } });
    await settle();
    await flushWidget();

    expect(text()).toContain('Only an active agent answers visitors');
  });

  it('shows the link but no controls to somebody who may only read', async () => {
    await mount('website-chat.view');

    http.expectOne(url).flush(endpoint('Active'), { headers: { ETag: '"v1"' } });
    await settle();
    await flushWidget();

    expect(text()).toContain('/chat/');
    expect(button('Disable chat')).toBeUndefined();
    expect(button('Publish chat')).toBeUndefined();
  });

  it('explains a refusal to somebody with no channel permission at all', async () => {
    await mount();
    await settle();

    expect(text()).toContain('do not have access to website chat');
  });
});
