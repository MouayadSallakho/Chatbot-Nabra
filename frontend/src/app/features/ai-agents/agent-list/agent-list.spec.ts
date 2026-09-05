import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { TenantApi } from '@app/core/api/tenant-api';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage } from '@app/testing/test-doubles';
import { AiAgentApi, AiAgentSummary } from '../ai-agent-api';
import { AgentList } from './agent-list';

describe('AgentList', () => {
  const tenantId = '11111111-1111-1111-1111-111111111111';
  const agentsUrl = `/api/tenants/${tenantId}/ai-agents`;

  function summary(overrides: Partial<AiAgentSummary> = {}): AiAgentSummary {
    return {
      id: crypto.randomUUID(),
      tenantId,
      name: 'Sales Assistant',
      status: 'Draft',
      createdAt: '2026-05-01T09:00:00+00:00',
      updatedAt: '2026-05-01T09:00:00+00:00',
      ...overrides,
    };
  }

  let fixture: ComponentFixture<AgentList>;
  let http: HttpTestingController;

  function mount(): void {
    TestBed.configureTestingModule({
      imports: [AgentList],
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

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AgentList);
    fixture.detectChanges();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /**
   * The pending list request.
   *
   * Matched by path rather than by full URL: `expectOne(string)` compares
   * against the URL including its query string, and this request always carries
   * paging parameters.
   */
  const expectList = () => http.expectOne((candidate) => candidate.url === agentsUrl);

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const rows = (): HTMLAnchorElement[] => [
    ...host().querySelectorAll<HTMLAnchorElement>('a.agent-row'),
  ];

  afterEach(() => http.verify());

  it('asks for the first page of the current workspace', () => {
    mount();

    const request = http.expectOne((candidate) => candidate.url === agentsUrl);

    expect(request.request.params.get('page')).toBe('1');

    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
  });

  it('shows a busy state while loading, announced rather than silent', async () => {
    mount();

    expect(host().querySelector('[aria-busy="true"]')).not.toBeNull();
    expect(host().textContent).toContain('Loading agents');

    expectList().flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await settle();

    expect(host().querySelector('[aria-busy="true"]')).toBeNull();
  });

  it('explains what an agent is when there are none, and offers the first step', async () => {
    // Not "No data". Somebody who has never seen this product needs to know what
    // the thing is before they can want one.
    mount();

    expectList().flush({ items: [], page: 1, pageSize: 20, totalCount: 0 });
    await settle();

    expect(host().textContent).toContain('No agents yet');
    expect(host().textContent).toContain('replies to customers');

    const create = host().querySelector<HTMLAnchorElement>('a[href="/ai-agents/new"]');

    expect(create?.textContent).toContain('Create your first agent');
  });

  it('lists the agents with their status and last change', async () => {
    mount();

    expectList().flush({
      items: [
        summary({ name: 'Sales Assistant', status: 'Active' }),
        summary({ name: 'Support Assistant', status: 'Disabled' }),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 2,
    });
    await settle();

    expect(rows()).toHaveLength(2);
    expect(host().textContent).toContain('Sales Assistant');
    expect(host().textContent).toContain('Active');
    expect(host().textContent).toContain('Disabled');
  });

  it('shows each status as a badge carrying its own word, not colour alone', async () => {
    mount();

    expectList().flush({
      items: [summary({ status: 'Active' })],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    await settle();

    const badge = host().querySelector('.badge');

    expect(badge?.textContent?.trim()).toBe('Active');
    expect(badge?.classList.contains('badge-success')).toBe(true);
  });

  it('links each row to that agent', async () => {
    mount();

    const agent = summary();

    expectList().flush({ items: [agent], page: 1, pageSize: 20, totalCount: 1 });
    await settle();

    expect(rows()[0].getAttribute('href')).toBe(`/ai-agents/${agent.id}`);
  });

  it('never renders instruction text, even if the server sent some', async () => {
    // The list endpoint does not return instructions. This is the guard against
    // the other half of that contract: a template that would happily display
    // them if a future change started sending them.
    mount();

    const secret = 'Never quote a discount above fifteen percent.';

    expectList().flush({
      items: [{ ...summary({ name: 'Sales Assistant' }), instructions: secret }],
      page: 1,
      pageSize: 20,
      totalCount: 1,
    });
    await settle();

    expect(host().textContent).toContain('Sales Assistant');
    expect(host().textContent).not.toContain(secret);
  });

  it('does not put identifiers on screen as primary content', async () => {
    mount();

    const agent = summary();

    expectList().flush({ items: [agent], page: 1, pageSize: 20, totalCount: 1 });
    await settle();

    // The id is in the link target, where it belongs, and nowhere in the text.
    expect(host().textContent).not.toContain(agent.id);
  });

  it('reports a failed load and offers a retry that actually refetches', async () => {
    mount();

    expectList().flush({}, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(host().textContent).toContain('Could not load agents');

    const retry = [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
      button.textContent?.includes('Try again'),
    );

    retry!.click();
    await settle();

    expectList().flush({ items: [summary()], page: 1, pageSize: 20, totalCount: 1 });
    await settle();

    expect(host().textContent).not.toContain('Could not load agents');
    expect(rows()).toHaveLength(1);
  });

  it('shows no raw server internals when a load fails', async () => {
    mount();

    expectList().flush(
      { detail: 'Npgsql.PostgresException: relation "ai_agents" does not exist' },
      { status: 500, statusText: 'Server Error' },
    );
    await settle();

    expect(host().textContent).not.toContain('Npgsql');
    expect(host().textContent).not.toContain('PostgresException');
  });

  describe('pagination', () => {
    async function loadTwoPages(): Promise<void> {
      mount();

      expectList().flush({
        items: [summary(), summary()],
        page: 1,
        pageSize: 2,
        totalCount: 3,
      });
      await settle();
    }

    it('says what part of the list is on screen', async () => {
      await loadTwoPages();

      expect(host().textContent).toContain('Showing 1–2 of 3');
      expect(host().textContent).toContain('Page 1 of 2');
    });

    it('moves to the next page and refetches', async () => {
      await loadTwoPages();

      const next = [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
        button.textContent?.includes('Next'),
      );

      next!.click();
      await settle();

      const request = http.expectOne((candidate) => candidate.url === agentsUrl);

      expect(request.request.params.get('page')).toBe('2');

      request.flush({ items: [summary()], page: 2, pageSize: 2, totalCount: 3 });
      await settle();

      expect(host().textContent).toContain('Showing 3–3 of 3');
    });

    it('disables the controls at the ends of the list', async () => {
      await loadTwoPages();

      const previous = [...host().querySelectorAll<HTMLButtonElement>('button')].find((button) =>
        button.textContent?.includes('Previous'),
      );

      expect(previous?.disabled).toBe(true);
    });

    it('offers no pager when everything fits on one page', async () => {
      mount();

      expectList().flush({ items: [summary()], page: 1, pageSize: 20, totalCount: 1 });
      await settle();

      expect(host().textContent).not.toContain('Page 1 of');
      expect(host().textContent).toContain('Showing 1–1 of 1');
    });
  });

  it('offers a create action once the list has agents', async () => {
    mount();

    expectList().flush({ items: [summary()], page: 1, pageSize: 20, totalCount: 1 });
    await settle();

    const create = host().querySelector<HTMLAnchorElement>('a[href="/ai-agents/new"]');

    expect(create?.textContent).toContain('Create agent');
  });
});
