import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { CHANNEL_PROBLEM_WORDS } from '../../channel-problems';
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
      greetings: { en: null, ar: null },
      launcherPosition: 'Right',
      theme: 'System',
    },
    allowedOrigins: [],
    maxAllowedOrigins: 20,
    installSnippet:
      '<script async src="https://omniai.test/widget/v1.js" data-nabra-key="aaaaaaaaaaaaaaaaaaaaaa"></script>',
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

    expect(text()).toContain('data-nabra-key="aaaaaaaaaaaaaaaaaaaaaa"');
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
          greetings: { en: null, ar: null },
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
    expect(preview.textContent).toContain('Acme Trading');
    expect(preview.textContent).toContain('Hello there');
    expect(preview.textContent).toContain('Visitors always see it dark');

    // Typing a colour repaints the preview before anything is saved.
    await setAccent('#ffe066');

    expect(stage.style.getPropertyValue('--chat-accent')).toBe('#ffe066');
    expect(stage.style.getPropertyValue('--chat-on-accent')).toBe('#111827');
    expect(stage.style.getPropertyValue('--chat-accent-edge')).toBe('#111827');
  });

  it('offers all four corners, and shows a widget already set to Start', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(
      configuration({
        widget: {
          greeting: null,
          accentColor: '#2563eb',
          greetings: { en: null, ar: null },
          launcherPosition: 'Start',
          theme: 'System',
        },
      }),
      { headers: { ETag: '"v1"' } },
    );
    await settle();

    const radios = [
      ...host().querySelectorAll<HTMLInputElement>('#widget-position input[type="radio"]'),
    ];

    expect(radios.map((radio) => radio.value)).toEqual(['Right', 'Left', 'Start', 'End']);
    // One group, each radio inside its own 44px label with its words and picture.
    expect(new Set(radios.map((radio) => radio.name)).size).toBe(1);
    expect(radios.every((radio) => radio.closest('label.choice'))).toBe(true);

    const checked = radios.find((radio) => radio.checked)!;

    expect(checked.value).toBe('Start');
    expect(checked.closest('label')?.textContent).toContain('right in Arabic pages');
    // An English page: Start is the left-hand corner, in the preview and in its picture.
    expect(host().querySelector('app-chat-launcher.is-left')).not.toBeNull();
    expect(
      host().querySelector('[data-testid="corner-Start"] .corner-picture')?.classList,
    ).toContain('is-left');
    expect(
      host().querySelector('[data-testid="corner-End"] .corner-picture')?.classList,
    ).not.toContain('is-left');
  });

  it('sends End as End', async () => {
    await mount('website-chat.view', 'website-chat.manage');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    host().querySelector<HTMLInputElement>('#widget-position input[value="End"]')!.click();
    await settle();

    // An English page: End is the right-hand corner.
    expect(host().querySelector('app-chat-launcher.is-left')).toBeNull();

    button('Save widget settings')!.click();

    const save = http.expectOne((request) => request.method === 'PUT' && request.url === url);

    expect(save.request.body.launcherPosition).toBe('End');
    save.flush(configuration(), { headers: { ETag: '"v2"' } });
    await settle();
  });

  it("says visitors see the workspace's name, and the preview shows it", async () => {
    await mount('website-chat.view', 'website-chat.manage');
    fixture.componentRef.setInput('agentName', 'Concierge');

    http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
    await settle();

    expect(host().querySelector('[data-testid="visitors-see-name"]')?.textContent).toContain(
      "your workspace's name",
    );
    expect(
      host().querySelector('[data-testid="widget-preview"] [data-testid="chat-title"]')
        ?.textContent,
    ).toContain('Acme Trading');
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

  describe('the greeting per language (Phase 39.1)', () => {
    const box = (id: string): HTMLTextAreaElement =>
      host().querySelector<HTMLTextAreaElement>(`#${id}`)!;

    async function typeInto(id: string, value: string): Promise<void> {
      box(id).value = value;
      box(id).dispatchEvent(new Event('input'));
      await settle();
    }

    it('shows English, Arabic and the default, each in its own direction', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(
        configuration({
          widget: {
            ...configuration().widget,
            greeting: 'Hello.',
            greetings: { en: 'Hi there!', ar: 'أهلًا بك!' },
          },
        }),
        { headers: { ETag: '"v1"' } },
      );
      await settle();

      expect(box('widget-greeting-en').value).toBe('Hi there!');
      expect(box('widget-greeting-en').getAttribute('dir')).toBe('ltr');
      expect(box('widget-greeting-ar').value).toBe('أهلًا بك!');
      expect(box('widget-greeting-ar').getAttribute('dir')).toBe('rtl');
      expect(box('widget-greeting-ar').getAttribute('lang')).toBe('ar');
      expect(box('widget-greeting').value).toBe('Hello.');
      expect(text()).toContain('Visitors whose language has no greeting of its own see this one.');
      // The preview shows what a visitor reading this page's language sees.
      expect(
        host().querySelector('[data-testid="widget-preview"] [data-testid="chat-greeting"]')
          ?.textContent,
      ).toContain('Hi there!');
    });

    it('falls back to the default in the preview when this language has none', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(
        configuration({
          widget: {
            ...configuration().widget,
            greeting: 'Hello.',
            greetings: { en: null, ar: 'أهلًا' },
          },
        }),
        { headers: { ETag: '"v1"' } },
      );
      await settle();

      expect(
        host().querySelector('[data-testid="widget-preview"] [data-testid="chat-greeting"]')
          ?.textContent,
      ).toContain('Hello.');
    });

    it('always sends both greetings, trimmed, an empty one as null', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
      await settle();

      await typeInto('widget-greeting-en', '  Hi!  ');
      await typeInto('widget-greeting-ar', '   ');

      button('Save widget settings')!.click();
      await settle();

      const save = http.expectOne(url);

      expect(save.request.body.greetings).toEqual({ en: 'Hi!', ar: null });
      save.flush(configuration(), { headers: { ETag: '"v2"' } });
      await settle();
    });

    it('caps each box at the server bound before anything is sent', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
      await settle();

      for (const id of ['widget-greeting-en', 'widget-greeting-ar', 'widget-greeting']) {
        expect(box(id).getAttribute('maxlength')).toBe('300');
      }

      await typeInto('widget-greeting-ar', 'ا'.repeat(301));

      expect(button('Save widget settings')!.disabled).toBe(true);
    });

    it('words a refusal by the greeting the server names, beside that box', async () => {
      // The channels' routes register these words; a component test does it itself.
      registerProblemWords(CHANNEL_PROBLEM_WORDS);
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
      await settle();

      await typeInto('widget-greeting-ar', 'مرحبًا');

      button('Save widget settings')!.click();
      await settle();

      http.expectOne(url).flush(
        {
          title: 'Refused',
          detail: 'The English sentence from the server.',
          code: 'website-chat.greeting.control_characters',
          field: 'greetings.ar',
        },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();
      await new Promise((resolve) => setTimeout(resolve, 0));

      const arabic = box('widget-greeting-ar');
      const error = host().querySelector('#widget-greeting-ar-error');

      expect(error?.textContent).toContain('The Arabic greeting has characters');
      expect(arabic.getAttribute('aria-invalid')).toBe('true');
      expect(arabic.getAttribute('aria-describedby')).toContain('widget-greeting-ar-error');
      expect(box('widget-greeting-en').getAttribute('aria-invalid')).toBeNull();
      expect(document.activeElement).toBe(arabic);
      expect(text()).not.toContain('The English sentence');
    });
  });

  describe('the colour inside the chat (Phase 39.5b)', () => {
    const stage = (): HTMLElement =>
      host().querySelector<HTMLElement>('[data-testid="widget-preview"] .preview-stage')!;

    it('keeps emerald where the colour fails, and says so in the preview', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
      await settle();

      // A navy: fine on the light ground, lost on the dark one.
      await setAccent('#1d3557');

      expect(stage().style.getPropertyValue('--chat-brand-light')).toBe('#1d3557');
      expect(stage().style.getPropertyValue('--chat-brand-dark')).toBe('');
      expect(host().querySelector('[data-testid="chat-check-light"]')?.textContent).toContain(
        'use your colour',
      );
      expect(host().querySelector('[data-testid="chat-check-dark"]')?.textContent).toContain(
        "Nabra's emerald",
      );
      expect(host().querySelector('[data-testid="preview-emerald"]')?.textContent).toContain(
        'In the dark look',
      );
    });

    it('says nothing about emerald when the colour holds everywhere it is seen', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http
        .expectOne(url)
        .flush(configuration({ widget: { ...configuration().widget, theme: 'Light' } }), {
          headers: { ETag: '"v1"' },
        });
      await settle();

      // Only the light look is checked when visitors always see it light.
      expect(host().querySelector('[data-testid="chat-check-dark"]')).toBeNull();
      expect(host().querySelector('[data-testid="chat-check-light"]')).not.toBeNull();
      expect(host().querySelector('[data-testid="preview-emerald"]')).toBeNull();
    });

    it('names both looks when the colour fails in each', async () => {
      await mount('website-chat.view', 'website-chat.manage');

      http.expectOne(url).flush(configuration(), { headers: { ETag: '"v1"' } });
      await settle();

      // A mid grey: neither ink reaches 4.5:1 on it, so it holds in neither look.
      await setAccent('#7a7a7a');

      expect(host().querySelector('[data-testid="preview-emerald"]')?.textContent).toContain(
        'inside the chat',
      );
    });
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
