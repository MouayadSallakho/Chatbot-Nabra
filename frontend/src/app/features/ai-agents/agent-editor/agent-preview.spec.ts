import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AiAgentApi, AiAgentStatus } from '../ai-agent-api';
import { AgentPreview } from './agent-preview';

/**
 * The preview panel.
 *
 * Two things are being protected. That every provider failure produces copy a
 * reader can act on, with nothing from the provider itself on screen. And that
 * preview never sends a precondition or consumes an entity tag, so it cannot
 * disturb the version the editor is holding for its saves.
 */
describe('AgentPreview', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const agentId = '22222222-2222-2222-2222-222222222222';
  const previewUrl = `/api/tenants/${tenantId}/ai-agents/${agentId}/preview`;

  let fixture: ComponentFixture<AgentPreview>;
  let http: HttpTestingController;

  function mount(): void {
    TestBed.configureTestingModule({
      imports: [AgentPreview],
      providers: [provideHttpClient(), provideHttpClientTesting(), AiAgentApi],
    });

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AgentPreview);
    fixture.componentRef.setInput('tenantId', tenantId);
    fixture.componentRef.setInput('aiAgentId', agentId);
    fixture.componentRef.setInput('agentName', 'Sales Assistant');
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const box = (): HTMLTextAreaElement =>
    host().querySelector<HTMLTextAreaElement>('[data-testid="chat-input"]')!;
  /** Selected by test id rather than by label, which changes while generating. */
  const sendButton = (): HTMLButtonElement =>
    host().querySelector<HTMLButtonElement>('[data-testid="chat-send"]')!;
  const frame = (): HTMLElement =>
    host().querySelector<HTMLElement>('[data-testid="preview-frame"]')!;

  async function ask(message = 'Which laptop suits a student?'): Promise<void> {
    box().value = message;
    box().dispatchEvent(new Event('input'));
    await settle();

    sendButton().click();
    await settle();
  }

  afterEach(() => http.verify());

  it('starts idle, with nothing pretending to be a conversation', () => {
    mount();

    expect(host().querySelector('[data-testid="chat-message"]')).toBeNull();
    expect(host().querySelector('[data-testid="chat-empty"]')).not.toBeNull();
    expect(sendButton().disabled).toBe(true);
    http.verify();
  });

  it('sends the message and shows the reply', async () => {
    mount();
    await ask();

    const request = http.expectOne(previewUrl);

    expect(request.request.body).toEqual({ message: 'Which laptop suits a student?' });

    request.flush({ reply: 'The 14-inch model suits most students.' });
    await settle();

    expect(host().textContent).toContain('Which laptop suits a student?');
    expect(host().textContent).toContain('The 14-inch model suits most students.');
  });

  it('never sends a precondition', async () => {
    // Preview changes nothing, so there is no write to protect. Requiring one
    // would teach clients the wrong thing about what the endpoint does.
    mount();
    await ask();

    const request = http.expectOne(previewUrl);

    expect(request.request.headers.has('If-Match')).toBe(false);

    request.flush({ reply: 'Hello.' });
    await settle();
  });

  it('shows a generating state while it waits', async () => {
    mount();
    await ask();

    expect(host().textContent).toContain('Sales Assistant is typing');
    expect(sendButton().disabled).toBe(true);

    http.expectOne(previewUrl).flush({ reply: 'Done.' });
    await settle();

    expect(host().textContent).not.toContain('is typing');
  });

  it('clears the box after a reply, ready for the next question', async () => {
    mount();
    await ask();

    http.expectOne(previewUrl).flush({ reply: 'Done.' });
    await settle();

    expect(box().value).toBe('');
  });

  describe('every lifecycle state can be previewed', () => {
    const states: readonly AiAgentStatus[] = ['Draft', 'Active', 'Disabled'];

    for (const status of states) {
      it(`works for a ${status} agent`, async () => {
        // Status governs whether an agent serves customers, not whether its
        // owner may test it. The panel takes no status input at all, which is
        // what makes this structurally true rather than merely currently true.
        mount();
        await ask(`Testing a ${status} agent`);

        http.expectOne(previewUrl).flush({ reply: 'Replied.' });
        await settle();

        expect(host().textContent).toContain('Replied.');
      });
    }
  });

  describe('failures', () => {
    async function fail(status: number, body: object = {}): Promise<void> {
      mount();
      await ask();

      http.expectOne(previewUrl).flush(body, { status, statusText: 'error' });
      await settle();
    }

    it('explains a rejected message (400) in the backend words', async () => {
      await fail(400, { detail: 'The message must not be empty.' });

      expect(host().textContent).toContain('The message must not be empty.');
    });

    it('explains an unusable provider answer (502) without naming the provider', async () => {
      await fail(502, { detail: 'OpenAI returned an empty completion.' });

      expect(host().textContent).toContain('could not be used');
      expect(host().textContent).not.toContain('OpenAI');
    });

    it('explains an unconfigured provider (503) usefully, as information', async () => {
      await fail(503);

      const alert = host().querySelector('[data-testid="chat-error"]')!;

      expect(alert.textContent).toContain('none is configured on this deployment');
      // A fact about the deployment, not something the reader did wrong.
      expect(alert.classList).toContain('alert-info');
      expect(alert.classList).not.toContain('alert-danger');
    });

    it('shows a provider that answered badly as an error', async () => {
      await fail(502, { detail: 'OpenAI returned an empty completion.' });

      expect(host().querySelector('[data-testid="chat-error"]')?.classList).toContain(
        'alert-danger',
      );
    });

    it('explains a timeout (504) and does not retry it', async () => {
      await fail(504);

      expect(host().textContent).toContain('too long');
      // Nothing further was sent. The first request may still be running and
      // still being billed; a second would double the cost of a bad situation.
      http.verify();
    });

    it('keeps the message so the reader can try again without retyping', async () => {
      mount();

      box().value = 'A question worth keeping';
      box().dispatchEvent(new Event('input'));
      await settle();

      sendButton().click();
      await settle();

      http.expectOne(previewUrl).flush({}, { status: 503, statusText: 'Service Unavailable' });
      await settle();

      expect(box().value).toBe('A question worth keeping');
    });

    it('reports an unreachable backend as a connection problem', async () => {
      mount();
      await ask();

      http.expectOne(previewUrl).error(new ProgressEvent('error'), { status: 0 });
      await settle();

      expect(host().textContent).toContain('could not be reached');
    });
  });

  it('says the transcript is not saved, rather than implying a history', async () => {
    mount();

    expect(host().textContent).toContain('nothing here is saved');
    expect(host().textContent).toContain('not given the earlier ones');
    http.verify();
  });

  it('warns when the editor holds unsaved changes', async () => {
    // Preview uses the saved name and instructions, so somebody testing an
    // unsaved edit would otherwise conclude their change had no effect.
    mount();
    fixture.componentRef.setInput('unsavedChanges', true);
    await settle();

    expect(host().textContent).toContain('Save your changes to try them here');
    http.verify();
  });

  describe('as the customer sees it', () => {
    it('is the shared chat, titled with the agent name and the honesty line', () => {
      mount();

      expect(host().querySelector('app-chat-panel.is-widget')).not.toBeNull();
      // The editor's own h1 stays the page's only one.
      const title = host().querySelector('[data-testid="chat-title"]')!;

      expect(title.tagName).toBe('P');
      expect(title.textContent).toContain('Sales Assistant');
      expect(host().querySelector('.chat-honesty')?.textContent).toContain(
        'Answers are generated by AI.',
      );
    });

    it('uses the widget default accent with a readable foreground until told otherwise', async () => {
      mount();

      expect(frame().style.getPropertyValue('--chat-accent')).toBe('#2563eb');
      expect(frame().style.getPropertyValue('--chat-on-accent')).toBe('#ffffff');

      // A pale accent gets the dark foreground, never white on yellow.
      fixture.componentRef.setInput('widget', {
        greeting: null,
        accentColor: '#f2c94c',
        launcherPosition: 'Right',
        theme: 'System',
      });
      await settle();

      expect(frame().style.getPropertyValue('--chat-on-accent')).toBe('#111827');
      // ...and, being pale on a white page, the launcher's edge.
      expect(frame().style.getPropertyValue('--chat-accent-edge')).toBe('#111827');
    });

    it('never paints a value that is not a colour', async () => {
      mount();
      fixture.componentRef.setInput('widget', {
        greeting: null,
        accentColor: 'red; background: url(x)',
        launcherPosition: 'Right',
        theme: 'System',
      });
      await settle();

      expect(frame().style.getPropertyValue('--chat-accent')).toBe('#2563eb');
      expect(frame().getAttribute('style')).not.toContain('url(');
    });

    it("shows the workspace's greeting as the widget does, not as an answer", async () => {
      mount();
      fixture.componentRef.setInput('widget', {
        greeting: 'Hello! Ask us about orders.',
        accentColor: '#0e7c72',
        launcherPosition: 'Right',
        theme: 'System',
      });
      await settle();

      expect(host().querySelector('[data-testid="chat-greeting"]')?.textContent).toContain(
        'Ask us about orders',
      );
      expect(host().querySelector('[data-testid="chat-message"]')).toBeNull();
    });

    it('keeps each message in its writer direction and the AI on its own side', async () => {
      mount();
      await ask('ما هي سياسة الإرجاع؟');

      http.expectOne(previewUrl).flush({ reply: 'You can return within 30 days.' });
      await settle();

      const customer = host().querySelector('[data-sender="Customer"] .chat-text');
      const ai = host().querySelector('[data-sender="AiAgent"] .chat-text');

      expect(customer?.getAttribute('dir')).toBe('auto');
      expect(ai?.getAttribute('dir')).toBe('auto');
      expect(ai?.textContent).toContain('return within 30 days');
    });

    it('sends on Enter and starts a new line on Shift+Enter, as the widget does', async () => {
      mount();

      box().value = 'Hello';
      box().dispatchEvent(new Event('input'));
      await settle();

      const shifted = new KeyboardEvent('keydown', {
        key: 'Enter',
        shiftKey: true,
        cancelable: true,
      });

      box().dispatchEvent(shifted);
      await settle();

      expect(shifted.defaultPrevented).toBe(false);
      http.expectNone(previewUrl);

      const plain = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });

      box().dispatchEvent(plain);
      await settle();

      expect(plain.defaultPrevented).toBe(true);
      http.expectOne(previewUrl).flush({ reply: 'Hi.' });
      await settle();
    });
  });
});
