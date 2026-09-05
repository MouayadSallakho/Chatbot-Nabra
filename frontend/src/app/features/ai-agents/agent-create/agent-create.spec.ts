import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage } from '@app/testing/test-doubles';
import { AiAgent, AiAgentApi } from '../ai-agent-api';
import { AgentCreate } from './agent-create';

describe('AgentCreate', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const agentId = '22222222-2222-2222-2222-222222222222';
  const agentsUrl = `/api/tenants/${tenantId}/ai-agents`;

  const created: AiAgent = {
    id: agentId,
    tenantId,
    name: 'Sales Assistant',
    instructions: 'Help customers choose suitable products.',
    status: 'Draft',
    createdAt: '2026-05-01T09:00:00+00:00',
    updatedAt: '2026-05-01T09:00:00+00:00',
  };

  let fixture: ComponentFixture<AgentCreate>;
  let http: HttpTestingController;
  let navigated: { url: unknown[]; state: unknown } | null;

  function mount(): void {
    navigated = null;

    TestBed.configureTestingModule({
      imports: [AgentCreate],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        AiAgentApi,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
      ],
    });

    TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    const router = TestBed.inject(Router);

    router.navigate = ((url: unknown[], extras?: { state?: unknown }) => {
      navigated = { url, state: extras?.state };

      return Promise.resolve(true);
    }) as Router['navigate'];

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AgentCreate);
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const nameBox = () => host().querySelector<HTMLInputElement>('#agent-name')!;
  const instructionsBox = () => host().querySelector<HTMLTextAreaElement>('#agent-instructions')!;
  const submit = () => host().querySelector<HTMLButtonElement>('button[type="submit"]')!;

  async function fill(name: string, instructions: string): Promise<void> {
    nameBox().value = name;
    nameBox().dispatchEvent(new Event('input'));
    instructionsBox().value = instructions;
    instructionsBox().dispatchEvent(new Event('input'));
    await settle();
  }

  afterEach(() => http.verify());

  it('sends nothing until both fields are supplied', async () => {
    mount();

    submit().click();
    await settle();

    expect(host().textContent).toContain('An agent name is required.');
    expect(host().textContent).toContain('Instructions are required.');
    http.verify();
  });

  it('posts the two fields the reader supplied, and only those', async () => {
    mount();
    await fill('Sales Assistant', 'Help customers choose suitable products.');

    submit().click();
    await settle();

    const request = http.expectOne(agentsUrl);

    expect(request.request.method).toBe('POST');
    // The identity, workspace, status and timestamps are the backend's to
    // decide, and none of them is expressible on this screen.
    expect(request.request.body).toEqual({
      name: 'Sales Assistant',
      instructions: 'Help customers choose suitable products.',
    });

    request.flush(created, { headers: { ETag: '"v1"' } });
    await settle();
  });

  it('hands the created agent and its entity tag to the editor', async () => {
    mount();
    await fill('Sales Assistant', 'Be helpful.');

    submit().click();
    await settle();

    http.expectOne(agentsUrl).flush(created, { headers: { ETag: '"v1"' } });
    await settle();

    expect(navigated?.url).toEqual(['/ai-agents', agentId]);
    // The tag is preserved rather than discarded, so the editor opens ready to
    // save without re-reading what it was just handed.
    expect(navigated?.state).toEqual({
      'omniai.createdAgent': { agent: created, etag: '"v1"' },
    });
  });

  it('prevents a second submission while the first is in flight', async () => {
    // Without this, a fast second click creates a second agent.
    mount();
    await fill('Sales Assistant', 'Be helpful.');

    submit().click();
    await settle();

    expect(submit().disabled).toBe(true);

    submit().click();
    await settle();

    // Exactly one request, not two.
    http.expectOne(agentsUrl).flush(created, { headers: { ETag: '"v1"' } });
    await settle();
  });

  it('reports a rejected value in the backend own words, and stays on the form', async () => {
    // A value the client happily accepts and the Domain does not. This is why
    // the frontend does not try to mirror every rule: the backend stays the
    // authority, and its wording is what the reader sees.
    mount();
    await fill('   ', 'Be helpful.');

    // Whitespace passes `required` on the raw value, so the request is sent.
    nameBox().value = 'Nameless';
    nameBox().dispatchEvent(new Event('input'));
    await settle();

    submit().click();
    await settle();

    http
      .expectOne(agentsUrl)
      .flush(
        { title: 'Invalid request details', detail: 'AI agent name must not be blank.' },
        { status: 400, statusText: 'Bad Request' },
      );
    await settle();

    expect(host().textContent).toContain('AI agent name must not be blank.');
    expect(navigated).toBeNull();
    // The reader's work is still there to correct.
    expect(nameBox().value).toBe('Nameless');
  });

  it('re-enables the button after a failure so the reader can retry', async () => {
    mount();
    await fill('Sales Assistant', 'Be helpful.');

    submit().click();
    await settle();

    http.expectOne(agentsUrl).flush({}, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(submit().disabled).toBe(false);
  });

  it('counts down the instruction capacity as a platform limit', async () => {
    mount();
    await fill('Sales Assistant', 'abc');

    expect(host().textContent).toContain('19997 of 20000 characters remaining');
    // Never described as a model or token limit, because it is neither.
    expect(host().textContent).not.toContain('token');
    expect(host().textContent).not.toContain('context');
  });

  it('never offers a field the backend owns', async () => {
    mount();

    const editable = [...host().querySelectorAll('input, textarea, select')].map(
      (control) => control.id,
    );

    expect(editable).toEqual(['agent-name', 'agent-instructions']);
  });
});
