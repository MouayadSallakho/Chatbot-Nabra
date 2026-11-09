import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TenantApi } from '@app/core/api/tenant-api';
import { PermissionApi } from '@app/core/authorization/permission-api';
import { InboxSignals } from '@app/core/sounds/inbox-signals';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { memoryStorage, permissionApiGranting } from '@app/testing/test-doubles';
import { ConversationsApi, TranscriptMessage } from '../conversations-api';
import { InboxState } from '../inbox-state';
import { ConversationComposer, MAX_REPLY_LENGTH } from './conversation-composer';

describe('ConversationComposer', () => {
  const tenantId = '11111111-1111-7111-8111-111111111111';
  const first = '22222222-2222-7222-8222-222222222222';
  const second = '33333333-3333-7333-8333-333333333333';
  const replyUrl = (id: string) => `/api/tenants/${tenantId}/conversations/${id}/messages`;

  let fixture: ComponentFixture<ConversationComposer>;
  let http: HttpTestingController;
  let sent: TranscriptMessage[];

  async function mount(): Promise<void> {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        ConversationsApi,
        InboxState,
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
        { provide: PermissionApi, useValue: permissionApiGranting('conversations.reply') },
      ],
    });

    await TestBed.inject(WorkspaceContextService).adopt({
      id: tenantId,
      name: 'Acme Trading',
      slug: 'acme',
      status: 'Active',
    });

    http = TestBed.inject(HttpTestingController);
    sent = [];
    fixture = TestBed.createComponent(ConversationComposer);
    fixture.componentRef.setInput('conversationId', first);
    fixture.componentInstance.sent.subscribe((message) => sent.push(message));
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const box = (): HTMLTextAreaElement => host().querySelector('textarea')!;
  const sendButton = (): HTMLButtonElement =>
    host().querySelector<HTMLButtonElement>('[data-testid="send-reply"]')!;

  async function write(text: string): Promise<void> {
    box().value = text;
    box().dispatchEvent(new Event('input'));
    await settle();
  }

  const stored = (text: string): TranscriptMessage => ({
    id: 'm1',
    sender: 'HumanAgent',
    text,
    sentAt: '2026-10-18T10:00:00Z',
    sequence: 5,
    sentBy: { userId: 'u1', email: 'sam@acme.test' },
  });

  afterEach(() => http.verify());

  it("tells the inbox signals a reply is this browser's own, before it is sent", async () => {
    await mount();

    const own = vi.spyOn(TestBed.inject(InboxSignals), 'ownAction');

    await write('On it.');
    sendButton().click();
    await settle();

    expect(own).toHaveBeenCalledWith(first);
    http.expectOne(replyUrl(first)).flush(stored('On it.'));
    await settle();
  });

  it('adds a line on Enter and sends on Ctrl+Enter', async () => {
    await mount();
    await write('We refunded it this morning.');

    box().dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    await settle();
    // verify() in afterEach would fail on a request here.
    http.expectNone(replyUrl(first));

    box().dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        ctrlKey: true,
        bubbles: true,
        cancelable: true,
      }),
    );
    await settle();

    const request = http.expectOne(replyUrl(first));

    expect(request.request.body.text).toBe('We refunded it this morning.');
    request.flush(stored('We refunded it this morning.'));
    await settle();

    expect(box().value).toBe('');
    expect(sent.map((message) => message.text)).toEqual(['We refunded it this morning.']);
  });

  it('is read-only while a reply is sending, so the text sent is the text on screen', async () => {
    await mount();
    await write('On its way.');

    sendButton().click();
    await settle();

    expect(box().readOnly).toBe(true);
    expect(sendButton().getAttribute('aria-busy')).toBe('true');

    http.expectOne(replyUrl(first)).flush(stored('On its way.'));
    await settle();

    expect(box().readOnly).toBe(false);
  });

  it('retries a send whose outcome is unknown with the same key, so one reply is stored', async () => {
    await mount();
    await write('Checking now.');

    sendButton().click();
    await settle();

    const firstTry = http.expectOne(replyUrl(first));
    const key = firstTry.request.body.clientMessageId;

    firstTry.error(new ProgressEvent('error'), { status: 0 });
    await settle();

    // The text is untouched and the reader is offered a retry.
    expect(box().value).toBe('Checking now.');
    expect(host().textContent).toContain('Your reply was not sent.');

    const retry = [...host().querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Try again'),
    )!;

    retry.click();
    await settle();

    const secondTry = http.expectOne(replyUrl(first));

    expect(secondTry.request.body.clientMessageId).toBe(key);
    secondTry.flush(stored('Checking now.'));
    await settle();
  });

  it('mints a new key once the text changes: it is a different reply', async () => {
    await mount();
    await write('Version one.');

    sendButton().click();
    await settle();

    const firstTry = http.expectOne(replyUrl(first));
    const key = firstTry.request.body.clientMessageId;

    firstTry.error(new ProgressEvent('error'), { status: 0 });
    await settle();

    await write('Version two.');
    sendButton().click();
    await settle();

    const secondTry = http.expectOne(replyUrl(first));

    expect(secondTry.request.body.clientMessageId).not.toBe(key);
    secondTry.flush(stored('Version two.'));
    await settle();
  });

  it('explains a refusal because the AI is answering, and offers no retry that would fail again', async () => {
    await mount();
    await write('Hello there.');

    sendButton().click();
    await settle();

    http
      .expectOne(replyUrl(first))
      .flush(
        { title: 'Conflict', detail: 'This conversation is being answered by the AI.' },
        { status: 409, statusText: 'Conflict' },
      );
    await settle();

    expect(host().textContent).toContain('The AI is answering this conversation now');
    expect(box().value).toBe('Hello there.');
    expect(
      [...host().querySelectorAll('button')].some((button) =>
        button.textContent?.includes('Try again'),
      ),
    ).toBe(false);
  });

  it('keeps each conversation its own draft', async () => {
    await mount();
    await write('Half a sentence for the first');

    fixture.componentRef.setInput('conversationId', second);
    await settle();

    expect(box().value).toBe('');

    await write('Something else');
    fixture.componentRef.setInput('conversationId', first);
    await settle();

    expect(box().value).toBe('Half a sentence for the first');
    expect(TestBed.inject(InboxState).hasDrafts()).toBe(true);
  });

  it('counts the characters left only near the limit', async () => {
    await mount();
    await write('Short.');

    expect(host().querySelector('#composer-count')!.textContent!.trim()).toBe('');

    await write('x'.repeat(MAX_REPLY_LENGTH - 10));

    expect(host().querySelector('#composer-count')!.textContent).toContain('10 characters left');
    expect(sendButton().disabled).toBe(false);
  });
});
