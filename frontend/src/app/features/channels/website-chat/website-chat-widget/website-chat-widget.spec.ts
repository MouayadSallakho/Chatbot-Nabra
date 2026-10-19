import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { WebChatAdminApi } from '../../website-chat-api';
import { WebsiteChatWidget } from './website-chat-widget';

describe('WebsiteChatWidget', () => {
  const tenantId = 'aaaaaaaa-aaaa-7aaa-8aaa-aaaaaaaaaaaa';
  const endpointId = 'bbbbbbbb-bbbb-7bbb-8bbb-bbbbbbbbbbbb';
  const url = `/api/tenants/${tenantId}/website-chat/${endpointId}/widget`;

  let fixture: ComponentFixture<WebsiteChatWidget>;
  let http: HttpTestingController;

  const configuration = (overrides: Record<string, unknown> = {}) => ({
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
    installSnippet:
      '<script async src="https://omniai.test/widget/v1.js" data-omniai-key="aaaaaaaaaaaaaaaaaaaaaa"></script>',
    loaderUrl: 'https://omniai.test/widget/v1.js',
    ...overrides,
  });

  async function mount(...permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        WebChatAdminApi,
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
    fixture = TestBed.createComponent(WebsiteChatWidget);
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

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  const originBox = (): HTMLInputElement | null => host().querySelector('#widget-origin');

  const listed = (): string[] =>
    [...host().querySelectorAll('.origin-value')].map((row) => row.textContent?.trim() ?? '');

  async function addOrigin(value: string): Promise<void> {
    const box = originBox()!;

    box.value = value;
    box.dispatchEvent(new Event('input'));
    await settle();

    button('Add')!.click();
    await settle();
  }

  afterEach(() => http.verify());

  it('shows the install snippet the server composed, rather than building one', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    expect(text()).toContain('data-omniai-key="aaaaaaaaaaaaaaaaaaaaaa"');
    expect(text()).toContain('https://omniai.test/widget/v1.js');
  });

  it('says plainly that a widget with no approved website will not appear', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    expect(text()).toContain('will not appear anywhere');
  });

  it('canonicalises a website as it is added, so the reader sees what will be stored', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    await addOrigin('HTTPS://WWW.Example.COM:443/');

    expect(listed()).toEqual(['https://www.example.com']);
  });

  it('refuses a wildcard, a path and plain http before anything is sent', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    await addOrigin('https://*.example.com');
    expect(text()).toContain('Wildcards are not accepted');

    await addOrigin('https://www.example.com/support');
    expect(text()).toContain('Remove the path');

    await addOrigin('http://www.example.com');
    expect(text()).toContain('Plain http is only accepted for localhost');

    expect(listed()).toEqual([]);
  });

  it('refuses the same website twice, because one entry is what makes removal exact', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    await addOrigin('https://www.example.com');
    await addOrigin('https://WWW.example.com/');

    expect(text()).toContain('already on the list');
    expect(listed()).toEqual(['https://www.example.com']);
  });

  it('sends the whole intended list with the version it read', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration({ allowedOrigins: ['https://old.example.com'] }), {
      headers: { ETag: '"v1"' },
    });
    await settle();

    button('Remove')!.click();
    await settle();

    await addOrigin('https://new.example.com');

    button('Save widget settings')!.click();
    await settle();

    const request = http.expectOne(url);

    expect(request.request.method).toBe('PUT');
    expect(request.request.headers.get('If-Match')).toBe('"v1"');
    expect(request.request.body.allowedOrigins).toEqual(['https://new.example.com']);
    expect(request.request.body.greeting).toBeNull();
    expect(request.request.body.accentColor).toBe('#2563eb');
    expect(request.request.body.launcherPosition).toBe('Right');
    expect(request.request.body.theme).toBe('System');

    request.flush(configuration({ allowedOrigins: ['https://new.example.com'] }), {
      headers: { ETag: '"v2"' },
    });
    await settle();

    expect(text()).toContain('Saved.');
    expect(listed()).toEqual(['https://new.example.com']);
  });

  it('sends a greeting trimmed, and an empty one as nothing at all', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    const greeting = host().querySelector<HTMLTextAreaElement>('#widget-greeting')!;

    greeting.value = '   ';
    greeting.dispatchEvent(new Event('input'));
    await settle();

    button('Save widget settings')!.click();
    await settle();

    const blank = http.expectOne(url);

    expect(blank.request.body.greeting).toBeNull();

    blank.flush(configuration(), { headers: { ETag: '"v2"' } });
    await settle();

    greeting.value = '  Hi — ask us anything.  ';
    greeting.dispatchEvent(new Event('input'));
    await settle();

    button('Save widget settings')!.click();
    await settle();

    const written = http.expectOne(url);

    expect(written.request.body.greeting).toBe('Hi — ask us anything.');

    written.flush(
      configuration({ widget: { ...configuration().widget, greeting: 'Hi — ask us anything.' } }),
      {
        headers: { ETag: '"v3"' },
      },
    );
    await settle();
  });

  it('keeps what the reader typed when the write is refused as stale', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    await addOrigin('https://new.example.com');

    button('Save widget settings')!.click();
    await settle();

    http
      .expectOne(url)
      .flush(
        { title: 'Precondition failed', detail: 'Changed elsewhere.' },
        { status: 412, statusText: 'Precondition Failed' },
      );
    await settle();

    expect(text()).toContain('changed somewhere else');
    expect(text()).toContain('what you have typed here is untouched');

    // The list the reader built is still on screen, and nothing was retried:
    // verify() in afterEach would fail on a second request.
    expect(listed()).toEqual(['https://new.example.com']);
  });

  it('re-reads only when nothing has been typed, on an explicit reload', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    await addOrigin('https://new.example.com');

    button('Save widget settings')!.click();
    await settle();

    http
      .expectOne(url)
      .flush({ title: 'Precondition failed' }, { status: 412, statusText: 'Precondition Failed' });
    await settle();

    button('Reload and discard my changes')!.click();
    await settle();

    http.expectOne(url).flush(configuration({ allowedOrigins: ['https://theirs.example.com'] }), {
      headers: { ETag: '"v9"' },
    });
    await settle();

    expect(listed()).toEqual(['https://theirs.example.com']);
  });

  it('refuses a silent re-read while there are unsaved changes', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    await addOrigin('https://new.example.com');

    // The page publishing the link changed the same aggregate. Reloading for the
    // reader would throw away an allowlist they are halfway through changing.
    fixture.componentInstance.refresh();
    await settle();

    expect(listed()).toEqual(['https://new.example.com']);
    expect(text()).toContain('changed somewhere else');
  });

  it('stops offering websites once the workspace limit is reached', async () => {
    const many = Array.from({ length: 3 }, (_, index) => `https://site${index}.example.com`);

    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration({ allowedOrigins: many, maxAllowedOrigins: 3 }), {
      headers: { ETag: '"v1"' },
    });
    await settle();

    expect(text()).toContain('maximum of 3 websites');
    expect(button('Add')!.disabled).toBe(true);
  });

  it('shows the settings but no controls to somebody who may only read', async () => {
    await mount('website-chat.view');

    http.expectOne(url).flush(configuration({ allowedOrigins: ['https://www.example.com'] }), {
      headers: { ETag: '"v1"' },
    });
    await settle();

    expect(listed()).toEqual(['https://www.example.com']);
    expect(button('Save widget settings')).toBeUndefined();
    expect(button('Remove')).toBeUndefined();
    expect(originBox()).toBeNull();
  });

  it('says that removal takes effect at once, and that this is not a rate limit', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    expect(text()).toContain('Removing a website takes effect at once');
    expect(text()).toContain('not a limit on how much anybody can send');
    expect(text()).toContain('does not use third-party cookies');
  });

  // ---- Phase 36.9: the preview and the colour checks ----------------------

  async function setAccent(value: string): Promise<void> {
    const box = host().querySelector<HTMLInputElement>('[data-testid="accent-hex"]')!;

    box.value = value;
    box.dispatchEvent(new Event('input'));
    await settle();
  }

  it('draws the preview with the real widget parts, from the form before it is saved', async () => {
    await mount('website-chat.view', 'website-chat.manage');
    fixture.componentRef.setInput('agentName', 'Concierge');

    http.expectOne(url).flush(
      configuration({
        widget: {
          greeting: 'Hello there',
          accentColor: '#2563eb',
          launcherPosition: 'Left',
          theme: 'Dark',
        },
      }),
      {
        headers: { ETag: '"v1"' },
      },
    );
    await settle();

    const preview = host().querySelector<HTMLElement>('[data-testid="widget-preview"]')!;
    const stage = preview.querySelector<HTMLElement>('.preview-stage')!;

    // The same components the iframe runs, inert, with no heading of their own.
    expect(preview.querySelector('app-chat-panel')).not.toBeNull();
    expect(preview.querySelector('app-chat-launcher.is-left')).not.toBeNull();
    expect(preview.querySelector('h1')).toBeNull();
    expect(stage.hasAttribute('inert')).toBe(true);
    expect(preview.textContent).toContain('Concierge');
    expect(preview.textContent).toContain('Hello there');
    expect(preview.textContent).toContain('Visitors always see it dark');

    // Typing a colour repaints the preview before anything is saved.
    await setAccent('#ffe066');

    expect(stage.style.getPropertyValue('--chat-accent')).toBe('#ffe066');
    expect(stage.style.getPropertyValue('--chat-on-accent')).toBe('#111827');
    expect(stage.style.getPropertyValue('--chat-accent-edge')).toBe('#111827');
  });

  it('checks a colour before it is used, and says what the rules did, in words', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    const checks = (): string =>
      host().querySelector('[data-testid="accent-checks"]')?.textContent ?? '';

    expect(checks()).toContain('Text on the colour');
    expect(checks()).toContain('readable');
    expect(checks()).toContain('easy to see');

    // A pale yellow: readable label, but a launcher that needs an outline on white.
    await setAccent('#ffe066');

    expect(checks()).toContain('gets an outline');

    // Something that is not a colour cannot be saved, and says how to write one.
    await setAccent('#12');

    expect(text()).toContain('six digits or letters');
    expect(button('Save widget settings')!.disabled).toBe(true);
  });

  it('knows when it holds unsaved changes, and says so beside Save', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    expect(host().querySelector('[data-testid="widget-unsaved"]')).toBeNull();

    await addOrigin('https://shop.example.com');

    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
    expect(host().querySelector('[data-testid="widget-unsaved"]')).not.toBeNull();
  });
});
