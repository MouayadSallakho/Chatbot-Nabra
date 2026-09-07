import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ConversationsApi } from '../conversations-api';
import { ConversationDetail } from './conversation-detail';

describe('ConversationDetail', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const conversationId = '22222222-2222-7222-8222-222222222222';

  const detailUrl = `/api/tenants/${tenantId}/conversations/${conversationId}`;
  const messagesUrl = `${detailUrl}/messages?limit=100`;

  let fixture: ComponentFixture<ConversationDetail>;
  let http: HttpTestingController;

  const detail = {
    id: conversationId,
    channel: 'WebChat',
    status: 'Open',
    startedAt: '2026-09-07T10:00:00Z',
    lastActivityAt: '2026-09-07T10:05:00Z',
    agentName: 'Concierge',
    messageCount: 2,
  };

  const message = (id: string, sender: string, text: string, sequence: number) => ({
    id,
    sender,
    text,
    sentAt: '2026-09-07T10:00:00Z',
    sequence,
  });

  async function mount(...permissions: string[]): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ConversationsApi,
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
    fixture = TestBed.createComponent(ConversationDetail);
    fixture.componentRef.setInput('conversationId', conversationId);
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

  const bubbles = (): string[] =>
    [...host().querySelectorAll('.message-text')].map((node) => node.textContent?.trim() ?? '');

  const button = (label: string): HTMLButtonElement | undefined =>
    [...host().querySelectorAll('button')].find((candidate) =>
      candidate.textContent?.includes(label),
    );

  afterEach(() => http.verify());

  it('renders the transcript in the order the server returned it', async () => {
    // Ingestion order, which is what the API pages by. Nothing here re-sorts by
    // the timestamp shown beside each message.
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail);
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [
        message('m1', 'Customer', 'First', 10),
        message('m2', 'AiAgent', 'Second', 11),
        message('m3', 'Customer', 'Third', 12),
      ],
      nextCursor: null,
    });
    await settle();

    expect(bubbles()).toEqual(['First', 'Second', 'Third']);
    expect(text()).toContain('Concierge');
  });

  it('renders script-like message text as text', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail);
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', '<script>alert(1)</script>', 10)],
      nextCursor: null,
    });
    await settle();

    expect(text()).toContain('<script>alert(1)</script>');
    expect(host().querySelector('script')).toBeNull();
  });

  it('loads a further page and keeps the pages in one continuous order', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail);
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'Page one', 10)],
      nextCursor: 10,
    });
    await settle();

    button('Load older messages')!.click();
    await settle();

    // The cursor is the stored ingestion sequence, not a page number.
    http.expectOne(`${detailUrl}/messages?limit=100&after=10`).flush({
      items: [message('m2', 'AiAgent', 'Page two', 11)],
      nextCursor: null,
    });
    await settle();

    expect(bubbles()).toEqual(['Page one', 'Page two']);
    expect(button('Load older messages')).toBeUndefined();
  });

  it('shows a conversation with no messages as empty rather than broken', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush({ ...detail, messageCount: 0 });
    await settle();

    http.expectOne(messagesUrl).flush({ items: [], nextCursor: null });
    await settle();

    expect(text()).toContain('no messages');
  });

  it('explains a conversation that is not there', async () => {
    await mount('conversations.view');

    http
      .expectOne(detailUrl)
      .flush(
        { title: 'Not found', detail: 'No such conversation.' },
        { status: 404, statusText: 'Not Found' },
      );
    await settle();

    expect(text()).toContain('Conversation not found');
  });

  it('refreshes by hand rather than polling', async () => {
    // Deliberate: a timer firing against every open admin tab is a load pattern
    // nobody asked for, and it still would not be realtime.
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail);
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'First', 10)],
      nextCursor: null,
    });
    await settle();

    button('Refresh')!.click();
    await settle();

    http.expectOne(detailUrl).flush(detail);
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'First', 10), message('m2', 'AiAgent', 'Newer', 11)],
      nextCursor: null,
    });
    await settle();

    expect(bubbles()).toEqual(['First', 'Newer']);
  });

  it('offers no reply box, because replying does not exist yet', async () => {
    await mount('conversations.view');

    http.expectOne(detailUrl).flush(detail);
    await settle();

    http.expectOne(messagesUrl).flush({
      items: [message('m1', 'Customer', 'First', 10)],
      nextCursor: null,
    });
    await settle();

    expect(host().querySelector('textarea')).toBeNull();
    expect(button('Send')).toBeUndefined();
  });

  it('explains a refusal rather than showing an empty transcript', async () => {
    await mount();
    await settle();

    expect(text()).toContain('do not have access to conversations');
  });
});
