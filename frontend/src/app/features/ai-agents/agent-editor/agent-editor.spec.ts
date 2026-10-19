import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { KnowledgeApi } from '@app/features/knowledge/knowledge-api';
import type { WebChatWidget } from '@app/features/channels/website-chat-api';
import { AgentChannelMap, AgentChannels } from '../agent-channels';
import { AiAgent, AiAgentApi } from '../ai-agent-api';
import { AgentEditor } from './agent-editor';

/**
 * The agent editor.
 *
 * The tests that matter most are the concurrency ones. They exercise the actual
 * editor -- not the API client -- because "the request carried If-Match" says
 * nothing about whether a reader's half-written instructions survive a rejected
 * save, and that is the behaviour the precondition exists to protect.
 */
describe('AgentEditor', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const agentId = '22222222-2222-2222-2222-222222222222';
  const agentUrl = `/api/tenants/${tenantId}/ai-agents/${agentId}`;
  const agentKnowledgeUrl = `${agentUrl}/knowledge`;

  const draft: AiAgent = {
    id: agentId,
    tenantId,
    name: 'Sales Assistant',
    instructions: 'Help customers choose suitable products.',
    status: 'Draft',
    createdAt: '2026-05-01T09:00:00+00:00',
    updatedAt: '2026-05-01T09:00:00+00:00',
  };

  let fixture: ComponentFixture<AgentEditor>;
  let http: HttpTestingController;

  /**
   * What the stand-in for `AgentChannels` answers: which channels use this
   * agent and the website widget's look. Through DI, never a module mock.
   */
  let channelMap: AgentChannelMap;
  let widget: WebChatWidget | null;

  const channelsDouble = {
    provide: AgentChannels,
    useValue: {
      load: () => Promise.resolve(channelMap),
      widgetFor: () => Promise.resolve(widget),
    },
  };

  beforeEach(() => {
    channelMap = { byAgent: new Map(), visible: true, incomplete: false };
    widget = null;
  });

  /** Mounts the editor and answers its initial read with `agent` at `etag`. */
  async function mount(agent: AiAgent = draft, etag = '"v1"'): Promise<void> {
    TestBed.configureTestingModule({
      imports: [AgentEditor],
      providers: [
        // Selecting a workspace asks what the reader may do there. Without a
        // double the request would be left open and every case in this file
        // would fail on verify() for a reason unrelated to what it asserts.
        { provide: PermissionApi, useValue: permissionApiGranting('*') },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        AiAgentApi,
        channelsDouble,
        // The editor hosts the knowledge section from Phase 17, which reads what
        // this agent may draw on. A real client over the testing backend, not a
        // double: the point of these tests is the editor as it actually renders.
        KnowledgeApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: TenantApi,
          useValue: { get: () => undefined, create: () => undefined },
        },
      ],
    });

    // The workspace is already resolved; this suite is about the editor.
    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(AgentEditor);
    fixture.componentRef.setInput('aiAgentId', agentId);
    fixture.detectChanges();

    http.expectOne(agentUrl).flush(agent, { headers: { ETag: etag } });
    await settle();

    // The knowledge section loads alongside the agent. Answered with an empty
    // workspace so it settles quietly: what it does with real documents is its
    // own suite's business, and leaving the request open would fail every case
    // in this file on verify() for a reason unrelated to what it asserts.
    flushKnowledge(etag);

    await settle();
  }

  /**
   * Answers the knowledge section's read of what this agent may draw on.
   *
   * The section is a child of the editor and loads when it is created, so this
   * has to be answered on the first render and again after any reload -- a
   * reload replaces the whole editor body, which re-creates the section and
   * makes it read the latest assignment. That is the intended behaviour: somebody
   * asking for the latest version should see the latest assignment too.
   */
  function flushKnowledge(etag: string): void {
    http
      .expectOne(agentKnowledgeUrl)
      .flush({ aiAgentId: agentId, documents: [] }, { headers: { ETag: etag } });
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const query = <T extends HTMLElement>(selector: string): T | null =>
    host().querySelector<T>(selector);

  const instructionsBox = (): HTMLTextAreaElement =>
    query<HTMLTextAreaElement>('#agent-instructions')!;
  const nameBox = (): HTMLInputElement => query<HTMLInputElement>('#agent-name')!;

  /** Types into a control the way a reader would. */
  async function type(element: HTMLInputElement | HTMLTextAreaElement, value: string) {
    element.value = value;
    element.dispatchEvent(new Event('input'));
    await settle();
  }

  function buttonLabelled(text: string): HTMLButtonElement | undefined {
    return [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes(text),
    );
  }

  /**
   * A control inside the status card.
   *
   * Scoped, because the confirmation dialog is always in the DOM -- closed, in
   * the `<dialog>` element -- and carries a button with the same label. Searching
   * the whole component would find that one and prove nothing about which
   * lifecycle actions the card actually offers.
   */
  function lifecycleButton(text: string): HTMLButtonElement | undefined {
    return [...host().querySelectorAll<HTMLButtonElement>('.behaviour-actions button')].find(
      (button) => button.textContent?.includes(text),
    );
  }

  afterEach(() => http.verify());

  describe('loading', () => {
    it('shows the agent and captures its entity tag', async () => {
      await mount();

      expect(query('h1')?.textContent).toContain('Sales Assistant');
      expect(nameBox().value).toBe('Sales Assistant');
      expect(instructionsBox().value).toBe(draft.instructions);
      // The tag is never shown, so it is observed through what a save sends.
      expect(query('input[value=\'"v1"\']')).toBeNull();
    });

    it('reports a failed load without pretending to have an agent', async () => {
      TestBed.configureTestingModule({
        imports: [AgentEditor],
        providers: [
          // Selecting a workspace asks what the reader may do there. Without a
          // double the request would be left open and every case in this file
          // would fail on verify() for a reason unrelated to what it asserts.
          { provide: PermissionApi, useValue: permissionApiGranting('*') },
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          AiAgentApi,
          channelsDouble,
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        ],
      });

      await TestBed.inject(WorkspaceContextService).adopt({
        id: tenantId,
        name: 'Acme',
        slug: 'acme',
        status: 'Active',
      });
      http = TestBed.inject(HttpTestingController);

      fixture = TestBed.createComponent(AgentEditor);
      fixture.componentRef.setInput('aiAgentId', agentId);
      fixture.detectChanges();

      http.expectOne(agentUrl).flush({}, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(host().textContent).toContain('Could not load this agent');
      expect(query('#agent-name')).toBeNull();
      // Still exactly one page heading, so focus and the page title have a home.
      expect(host().querySelectorAll('h1')).toHaveLength(1);
    });
  });

  it('asks nothing of the server for somebody who may not see agents', async () => {
    TestBed.configureTestingModule({
      imports: [AgentEditor],
      providers: [
        { provide: PermissionApi, useValue: permissionApiGranting('knowledge.view') },
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        AiAgentApi,
        channelsDouble,
        KnowledgeApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme',
      slug: 'acme',
      status: 'Active',
    });
    http = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(AgentEditor);
    fixture.componentRef.setInput('aiAgentId', agentId);
    await settle();

    http.expectNone(agentUrl);
    expect(query('[data-testid="agent-denied"]')).not.toBeNull();
    expect(host().querySelectorAll('h1')).toHaveLength(1);
  });

  describe('the sections and the customer view', () => {
    it('lays the form out in four numbered sections, in order', async () => {
      await mount();

      const headings = [...host().querySelectorAll('.authoring-main .form-section-title')].map(
        (heading) => heading.textContent?.trim(),
      );

      expect(headings).toEqual(['Identity', 'Instructions', 'Behaviour', 'Knowledge']);
    });

    it("keeps instructions in the writer's direction", async () => {
      await mount();

      expect(instructionsBox().getAttribute('dir')).toBe('auto');
      expect(nameBox().getAttribute('dir')).toBe('auto');
    });

    it('says where the agent answers, linking each channel', async () => {
      channelMap = {
        byAgent: new Map([
          [
            agentId,
            [
              {
                channel: 'WebChat',
                id: 'w1',
                status: 'Active',
                link: ['/channels/website-chat', 'w1'],
              },
            ],
          ],
        ]),
        visible: true,
        incomplete: false,
      };
      await mount();

      const link = query<HTMLAnchorElement>('[data-testid="agent-channels"] a');

      expect(link?.textContent).toContain('Website chat');
      expect(link?.getAttribute('href')).toBe('/channels/website-chat/w1');
    });

    it("draws the preview in the website widget's real accent and greeting", async () => {
      widget = {
        greeting: 'Hello from Acme.',
        accentColor: '#0e7c72',
        launcherPosition: 'Right',
        theme: 'System',
      };
      await mount();

      const frame = query<HTMLElement>('.customer-view')!;

      expect(frame.style.getPropertyValue('--cv-accent')).toBe('#0e7c72');
      expect(query('[data-testid="preview-greeting"]')?.textContent).toContain('Hello from Acme.');
      expect(query('[data-testid="preview-look"]')?.textContent).toContain("website chat's colour");
      expect(query('.cv-title')?.textContent).toContain('Sales Assistant');
    });

    it('says it shows the default look when no website chat uses the agent', async () => {
      await mount();

      expect(query('[data-testid="preview-look"]')?.textContent).toContain('default colour');
      expect(query('[data-testid="preview-greeting"]')).toBeNull();
    });

    it('tells the preview about any unsaved edit, name or instructions', async () => {
      await mount();

      expect(query('[data-testid="preview-unsaved"]')).toBeNull();

      await type(nameBox(), 'Sales Assistant 2');

      expect(query('[data-testid="preview-unsaved"]')).not.toBeNull();
    });
  });

  describe('renaming', () => {
    it('sends If-Match and adopts the new tag', async () => {
      await mount();

      await type(nameBox(), 'Support Assistant');
      buttonLabelled('Save name')!.click();

      const rename = http.expectOne(`${agentUrl}/name`);

      expect(rename.request.headers.get('If-Match')).toBe('"v1"');
      expect(rename.request.body).toEqual({ name: 'Support Assistant' });

      rename.flush({ ...draft, name: 'Support Assistant' }, { headers: { ETag: '"v2"' } });
      await settle();

      // The next save must use the advanced tag, not the one it started with.
      await type(nameBox(), 'Renamed again');
      buttonLabelled('Save name')!.click();

      const second = http.expectOne(`${agentUrl}/name`);

      expect(second.request.headers.get('If-Match')).toBe('"v2"');

      second.flush({ ...draft, name: 'Renamed again' }, { headers: { ETag: '"v3"' } });
      await settle();
    });

    it('sends nothing when the name is unchanged', async () => {
      // A save that rewrites the same value would still move updatedAt and
      // advance the version, making somebody else's open editor stale for
      // nothing.
      await mount();

      const save = buttonLabelled('Save name')!;

      expect(save.disabled).toBe(true);

      save.click();
      await settle();

      http.verify();
    });

    it('re-enables saving only once the value actually differs', async () => {
      await mount();

      await type(nameBox(), 'Something else');
      expect(buttonLabelled('Save name')!.disabled).toBe(false);

      await type(nameBox(), draft.name);
      expect(buttonLabelled('Save name')!.disabled).toBe(true);
    });
  });

  describe('editing instructions', () => {
    it('sends If-Match and adopts the new tag', async () => {
      await mount();

      await type(instructionsBox(), 'Answer support questions.\n\nEscalate billing.');
      buttonLabelled('Save instructions')!.click();

      const save = http.expectOne(`${agentUrl}/instructions`);

      expect(save.request.headers.get('If-Match')).toBe('"v1"');
      expect(save.request.body).toEqual({
        instructions: 'Answer support questions.\n\nEscalate billing.',
      });

      save.flush(
        { ...draft, instructions: 'Answer support questions.\n\nEscalate billing.' },
        { headers: { ETag: '"v2"' } },
      );
      await settle();

      expect(host().textContent).not.toContain('Unsaved changes');
    });

    it('marks the editor dirty while there are unsaved edits', async () => {
      await mount();

      expect(host().textContent).not.toContain('Unsaved changes');

      await type(instructionsBox(), 'Changed.');

      expect(host().textContent).toContain('Unsaved changes');
    });

    it('discards edits only when asked', async () => {
      await mount();

      await type(instructionsBox(), 'Changed.');
      buttonLabelled('Discard')!.click();
      await settle();

      expect(instructionsBox().value).toBe(draft.instructions);
      http.verify();
    });
  });

  describe('lifecycle', () => {
    it('activates a draft with If-Match and advances the tag', async () => {
      await mount();

      lifecycleButton('Activate agent')!.click();

      const activate = http.expectOne(`${agentUrl}/activate`);

      expect(activate.request.method).toBe('POST');
      expect(activate.request.headers.get('If-Match')).toBe('"v1"');

      activate.flush({ ...draft, status: 'Active' }, { headers: { ETag: '"v2"' } });
      await settle();

      expect(query('[data-testid="agent-status"]')?.textContent?.trim()).toBe('Active');
      expect(query('.lifecycle-step[aria-current="step"]')?.textContent?.trim()).toBe('Active');
      expect(lifecycleButton('Disable agent')).toBeDefined();
    });

    it('offers no disable action for a draft, because the backend would refuse it', async () => {
      await mount();

      expect(lifecycleButton('Disable agent')).toBeUndefined();
      expect(lifecycleButton('Activate agent')).toBeDefined();
    });

    it('disables an active agent, after confirmation, with If-Match', async () => {
      await mount({ ...draft, status: 'Active' });

      // jsdom does not implement HTMLDialogElement.showModal, so it is recorded
      // here rather than skipped -- otherwise "nothing was sent" would pass even
      // if the dialog had never been asked to open.
      let opened = false;
      const dialog = host().querySelector('dialog') as HTMLDialogElement;
      dialog.showModal = () => {
        opened = true;
      };

      lifecycleButton('Disable agent')!.click();
      await settle();

      expect(opened).toBe(true);

      // The confirmation is a real modal dialog; nothing is sent until it is
      // accepted.
      http.verify();

      fixture.componentInstance['disable']();
      await settle();

      const disable = http.expectOne(`${agentUrl}/disable`);

      expect(disable.request.headers.get('If-Match')).toBe('"v1"');

      disable.flush({ ...draft, status: 'Disabled' }, { headers: { ETag: '"v2"' } });
      await settle();

      expect(query('[data-testid="agent-status"]')?.textContent?.trim()).toBe('Disabled');
    });

    it('explains a lifecycle refusal as a state problem, not a stale version', async () => {
      await mount();

      lifecycleButton('Activate agent')!.click();

      http
        .expectOne(`${agentUrl}/activate`)
        .flush(
          { title: 'Operation not allowed', detail: 'This agent cannot be activated right now.' },
          { status: 409, statusText: 'Conflict' },
        );
      await settle();

      expect(host().textContent).toContain('This agent cannot be activated right now.');
      // Explicitly NOT the stale-version panel: a 409 is not fixed by reloading
      // and re-saving the same request.
      expect(host().textContent).not.toContain('updated somewhere else');

      // A refusal about state means the shown status may be behind, so the
      // editor re-reads quietly.
      http.expectOne(agentUrl).flush(draft, { headers: { ETag: '"v1"' } });
      await settle();
    });
  });

  // ---------------------------------------------------------------------------
  // The conflict behaviour this phase exists to get right
  // ---------------------------------------------------------------------------
  describe('when a save is rejected as stale (412)', () => {
    /** Loads at v1, edits the instructions, and has the save refused. */
    async function conflict(): Promise<string> {
      await mount(draft, '"v1"');

      const mine = 'My careful rewrite that must not be lost.';

      await type(instructionsBox(), mine);
      buttonLabelled('Save instructions')!.click();

      const save = http.expectOne(`${agentUrl}/instructions`);

      expect(save.request.headers.get('If-Match')).toBe('"v1"');

      save.flush(
        { title: 'AI agent has changed', detail: 'The agent is at a newer version.' },
        { status: 412, statusText: 'Precondition Failed' },
      );
      await settle();

      return mine;
    }

    it('keeps the unsaved text exactly as the reader left it', async () => {
      const mine = await conflict();

      // The single most important assertion in this suite. Their work is still
      // on screen and untouched.
      expect(instructionsBox().value).toBe(mine);
    });

    it('explains what happened without showing a status code', async () => {
      await conflict();

      expect(host().textContent).toContain('updated somewhere else');
      expect(host().textContent).toContain('have not been overwritten');
      expect(host().textContent).not.toContain('412');
    });

    it('never retries the rejected save automatically', async () => {
      await conflict();

      // Nothing further was sent. A retry would reapply the reader's change over
      // whatever the other writer meant, which is the loss being prevented.
      http.verify();
    });

    it('blocks further saving until the version is refreshed', async () => {
      await conflict();

      // The tag was dropped, so no mutation can be attempted with a version
      // already known to be stale.
      expect(buttonLabelled('Save instructions')!.disabled).toBe(true);
    });

    it('offers a reload, which performs a fresh read and captures the new tag', async () => {
      await conflict();

      // The reader deliberately accepts losing their edits.
      fixture.componentInstance['confirmReplace'] = () => true;

      buttonLabelled('Reload latest version')!.click();
      await settle();

      const reload = http.expectOne(agentUrl);

      expect(reload.request.method).toBe('GET');

      reload.flush(
        { ...draft, instructions: "Somebody else's version." },
        { headers: { ETag: '"v7"' } },
      );
      await settle();

      flushKnowledge('"v7"');
      await settle();

      expect(instructionsBox().value).toBe("Somebody else's version.");
      expect(host().textContent).not.toContain('updated somewhere else');

      // The new tag is in use, and the stale one is gone for good.
      await type(instructionsBox(), 'Now editing the latest.');
      buttonLabelled('Save instructions')!.click();

      const next = http.expectOne(`${agentUrl}/instructions`);

      expect(next.request.headers.get('If-Match')).toBe('"v7"');

      next.flush(draft, { headers: { ETag: '"v8"' } });
      await settle();
    });

    it('does not reload when the reader declines to lose their work', async () => {
      const mine = await conflict();

      fixture.componentInstance['confirmReplace'] = () => false;

      buttonLabelled('Reload latest version')!.click();
      await settle();

      expect(instructionsBox().value).toBe(mine);
      http.verify();
    });

    it('treats a missing precondition (428) the same way, defensively', async () => {
      // The backend's 428 is defence in depth -- the client should never send an
      // unconditional mutation. If it somehow does, the response must not be
      // mistaken for a routine error that invites a retry.
      await mount();

      await type(nameBox(), 'Renamed');
      buttonLabelled('Save name')!.click();

      http
        .expectOne(`${agentUrl}/name`)
        .flush(
          { title: 'Precondition required' },
          { status: 428, statusText: 'Precondition Required' },
        );
      await settle();

      expect(host().textContent).toContain('updated somewhere else');
      expect(nameBox().value).toBe('Renamed');
    });
  });
});
