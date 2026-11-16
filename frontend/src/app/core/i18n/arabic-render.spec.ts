// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync } from 'node:fs';
import { Component, LOCALE_ID, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { clearTranslations, loadTranslations } from '@angular/localize';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';

/**
 * A few real renders in Arabic, with the real catalogue.
 *
 * Arabic is loaded the way main.ts loads it -- translations first, then the
 * modules -- so every module under test is imported dynamically below. A static
 * import would evaluate module-level messages (the voice words, the navigation)
 * in English before the catalogue exists, which is precisely the mistake the
 * startup order prevents in the application.
 */
const CATALOGUE: { translations: Record<string, string> } = JSON.parse(
  readFileSync('src/locale/messages.ar.json', 'utf8'),
);

/** Text as a reader sees it: whitespace collapsed, bidi isolates included. */
const text = (element: Element | null | undefined): string =>
  (element?.textContent ?? '').replace(/\s+/g, ' ').trim();

describe('Arabic renders', () => {
  beforeAll(() => {
    // Specs share one module graph in this runner; earlier files may already have
    // evaluated the modules below in English. A fresh registry evaluates them
    // again, after the catalogue, as main.ts does.
    vi.resetModules();
    loadTranslations(CATALOGUE.translations);
    registerLocaleData(localeAr, 'ar');
    document.documentElement.lang = 'ar';
    document.documentElement.dir = 'rtl';
  });

  afterAll(() => {
    // And the reverse on the way out: the modules evaluated here hold Arabic, so
    // the files that run after this one must get fresh, English ones.
    clearTranslations();
    vi.resetModules();
    document.documentElement.lang = '';
    document.documentElement.removeAttribute('dir');
  });

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [{ provide: LOCALE_ID, useValue: 'ar' }] });
  });

  describe('the pager', () => {
    let Pagination: Type<unknown>;

    beforeAll(async () => {
      ({ Pagination } = await import('@app/shared/pagination/pagination'));
    });

    async function render(total: number, page = 1): Promise<HTMLElement> {
      const fixture = TestBed.createComponent(Pagination);
      fixture.componentRef.setInput('page', page);
      fixture.componentRef.setInput('pageSize', 20);
      fixture.componentRef.setInput('total', total);
      fixture.componentRef.setInput('items', 'customers');
      fixture.detectChanges();
      await fixture.whenStable();

      return fixture.nativeElement as HTMLElement;
    }

    it('says "21–40 of 57 customers" with the many form, and keeps the range left to right', async () => {
      const root = await render(57, 2);
      const summary = root.querySelector('[data-testid="pager-summary"]');

      expect(text(summary)).toBe('21–40 من 57 عميلًا');
      expect(summary?.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
      expect(root.querySelector('nav')?.getAttribute('aria-label')).toBe('صفحات العملاء');
      expect(text(root.querySelector('.pager-controls'))).toContain('الصفحة 2 من 3');
      expect(text(root.querySelector('.pager-controls'))).toContain('السابق');
      expect(text(root.querySelector('.pager-controls'))).toContain('التالي');
    });

    it('uses the right Arabic noun form for one, two, a few and a hundred', async () => {
      expect(text((await render(1)).querySelector('[data-testid="pager-summary"]'))).toBe(
        '1–1 من عميل واحد',
      );
      expect(text((await render(2)).querySelector('[data-testid="pager-summary"]'))).toBe(
        '1–2 من عميلَين',
      );
      expect(text((await render(5)).querySelector('[data-testid="pager-summary"]'))).toBe(
        '1–5 من 5 عملاء',
      );
      expect(text((await render(100)).querySelector('[data-testid="pager-summary"]'))).toBe(
        '1–20 من 100 عميل',
      );
    });
  });

  it('names the two voices with the approved Arabic words', async () => {
    const { VoiceChip } = await import('@app/shared/voice/voice-chip');

    const ai = TestBed.createComponent(VoiceChip);
    ai.componentRef.setInput('voice', 'ai');
    ai.detectChanges();

    const human = TestBed.createComponent(VoiceChip);
    human.componentRef.setInput('voice', 'human');
    human.detectChanges();

    expect(text(ai.nativeElement)).toBe('الذكاء الاصطناعي');
    expect(text(human.nativeElement)).toBe('الفريق');
  });

  it('resolves module-level messages, such as the navigation, in Arabic', async () => {
    const { NAV_ITEMS, NAV_GROUP_LABELS } = await import('@app/layout/navigation');

    expect(NAV_ITEMS[0].label).toBe('نظرة عامة');
    expect(NAV_ITEMS.find((item) => item.path === 'conversations')?.label).toBe('صندوق المحادثات');
    expect(NAV_GROUP_LABELS.Conversations).toBe('المحادثات');
    expect(NAV_GROUP_LABELS.AI).toBe('الذكاء الاصطناعي');
    expect(NAV_GROUP_LABELS.Insights).toBe('الرؤى');
    expect(NAV_GROUP_LABELS.Workspace).toBe('مساحة العمل');
  });

  it('names the product نبرة, in the wordmark and in the tab title', async () => {
    const { PRODUCT_NAME } = await import('@app/core/brand/brand');
    const { AppTitleStrategy } = await import('@app/core/title/app-title-strategy');

    @Component({ template: '' })
    class Blank {}

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'overview', title: 'نظرة عامة', component: Blank }]),
        { provide: TitleStrategy, useClass: AppTitleStrategy },
      ],
    });
    await TestBed.inject(Router).navigateByUrl('/overview');

    expect(PRODUCT_NAME).toBe('نبرة');
    expect(TestBed.inject(Title).getTitle()).toBe('نظرة عامة · نبرة');
  });

  it('counts unread notifications with Arabic plurals', async () => {
    const { unreadText } = await import('@app/core/notifications/notification-center');

    expect(unreadText(0, 'ar')).toBe('لا توجد إشعارات غير مقروءة');
    expect(unreadText(2, 'ar')).toBe('إشعاران غير مقروءين');
    expect(unreadText(7, 'ar')).toBe('7 إشعارات غير مقروءة');
    expect(unreadText(57, 'ar')).toBe('57 إشعارًا غير مقروء');
  });

  it('writes registration, its story panel and its example in Arabic, with Western digits', async () => {
    const { Register } = await import('@app/features/auth/register/register');
    const { provideHttpClient } = await import('@angular/common/http');
    const { provideHttpClientTesting } = await import('@angular/common/http/testing');

    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
    const fixture = TestBed.createComponent(Register);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(text(root.querySelector('h1'))).toBe('أنشئ حسابك');
    expect(text(root.querySelector('#password-hint'))).toBe(
      '12 حرفًا على الأقل. العبارة التي يسهل تذكّرها خيار جيد.',
    );
    expect(text(root.querySelector('.entry-tagline'))).toBe('صوتان… بنبرة واحدة');
    expect(text(root.querySelector('.entry-promise'))).toContain('الذكاء الاصطناعي');
    expect(root.querySelector('[data-testid="entry-lockup"]')?.getAttribute('aria-label')).toBe(
      'نبرة',
    );
    expect(text(root.querySelector('[data-testid="entry-language"]'))).toBe('English العربية');
  });

  describe('knowledge and the public chat (Phase 38.3)', () => {
    it('says a broken file limit in Arabic, with the bound in Western digits', async () => {
      const { HttpErrorResponse } = await import('@angular/common/http');
      const { toApiError } = await import('@app/core/api/api-error');
      const { registerProblemWords } = await import('@app/core/problems/problem-words');
      const { KNOWLEDGE_PROBLEM_WORDS } =
        await import('@app/features/knowledge/knowledge-problems');

      registerProblemWords(KNOWLEDGE_PROBLEM_WORDS);

      const refuse = (code: string, limit?: number) =>
        toApiError(
          new HttpErrorResponse({
            status: 422,
            error: { title: 'Refused', detail: 'The English sentence.', code, limit },
          }),
        ).message;

      expect(refuse('knowledge.file.too_many_pages', 500)).toBe(
        'عدد صفحات الملف أكبر من الحد المسموح به، وهو 500. قسّمه وارفع الجزء المهم منه.',
      );
      expect(refuse('knowledge.file.too_large', 10485760)).toContain('10');
      expect(refuse('knowledge.file.too_large', 10485760)).toContain('أقصى ما تقبله نبرة');
      expect(refuse('knowledge.file.encrypted')).toBe(
        'ملف PDF هذا محميّ بكلمة مرور. أزل كلمة المرور ثم ارفعه مرة أخرى.',
      );
      expect(refuse('knowledge.file.type_mismatch')).toContain('لا يطابق نوعه');
    });

    it('says the indexing states and reasons in Arabic', async () => {
      const { indexFailureReason, knowledgeStateWord } =
        await import('@app/features/knowledge/knowledge-words');

      expect(knowledgeStateWord('Indexing')).toBe('تجري الفهرسة الآن');
      expect(knowledgeStateWord('Stopped')).toBe('توقفت الفهرسة');
      expect(indexFailureReason('provider_timeout')).toBe(
        'استغرق مزوّد الذكاء الاصطناعي وقتًا أطول من اللازم للرد.',
      );
      expect(indexFailureReason(null)).toBe('حدث خطأ أثناء الفهرسة.');
    });

    it('writes the visitor chat in Arabic: the notice, the placeholder, Send and New chat', async () => {
      const { ChatPanel } = await import('@app/shared/chat/chat-panel');
      const fixture = TestBed.createComponent(ChatPanel);

      fixture.componentRef.setInput('workspaceName', 'Northwind');
      fixture.componentRef.setInput('canSend', true);
      fixture.componentRef.setInput('hasConversation', true);
      fixture.componentRef.setInput('failure', {
        kind: 'unknown',
        status: 500,
        message: 'تعذّر الرد على هذه الرسالة.',
      });
      fixture.detectChanges();
      await fixture.whenStable();
      const root = fixture.nativeElement as HTMLElement;

      expect(text(root.querySelector('[data-testid="chat-title"]'))).toBe('Northwind');
      expect(text(root.querySelector('.chat-honesty'))).toBe('الإجابات يولّدها الذكاء الاصطناعي.');
      expect(root.querySelector('textarea')?.getAttribute('placeholder')).toBe('اكتب رسالتك…');
      expect(text(root.querySelector('[data-testid="chat-send"]'))).toBe('إرسال');
      expect(text(root.querySelector('[data-testid="chat-new"]'))).toBe('محادثة جديدة');
      expect(text(root.querySelector('[data-testid="chat-error"]'))).toContain('تعذّر الرد');
    });

    it('offers the Start and End corners in Arabic', async () => {
      const ar = CATALOGUE.translations;

      expect(ar['widget.position.start']).toContain('اليمين في الصفحات العربية');
      expect(ar['widget.position.end']).toContain('اليسار في الصفحات العربية');
    });
  });

  describe('knowledge and AI agents in Calm Glass (Phase 39.5a)', () => {
    it("speaks the preview's three voices in Arabic, from the visitor's side", async () => {
      const { ChatTranscript } = await import('@app/shared/chat/chat-transcript');
      const fixture = TestBed.createComponent(ChatTranscript);

      fixture.componentRef.setInput('agentName', 'Concierge');
      fixture.componentRef.setInput('messages', [
        { id: '1', sender: 'Customer', text: 'أين طلبي؟', sentAt: '' },
        { id: '2', sender: 'AiAgent', text: 'لحظة من فضلك.', sentAt: '' },
        { id: '3', sender: 'HumanAgent', text: 'يُشحن اليوم.', sentAt: '' },
      ]);
      fixture.componentRef.setInput('typing', true);
      fixture.detectChanges();
      await fixture.whenStable();
      const root = fixture.nativeElement as HTMLElement;
      const [mine, ai, person] = [...root.querySelectorAll('[data-testid="chat-message"]')];

      expect(text(mine)).toBe('أنت: أين طلبي؟');
      expect(ai.querySelector('app-orb')).not.toBeNull();
      expect(text(ai.querySelector('.chat-sender'))).toBe('Concierge');
      expect(text(person.querySelector('.chat-sender'))).toBe('فريق الدعم');
      expect(person.querySelector('.chat-mark-team')).not.toBeNull();
      expect(text(root.querySelectorAll('[data-testid="chat-handoff-divider"]')[0])).toBe(
        'انضم أحد أعضاء فريق الدعم',
      );
      expect(text(root.querySelector('[data-testid="chat-typing"]'))).toBe('Concierge يكتب…');
      // Each message keeps its own direction inside the right-to-left page.
      expect(mine.querySelector('.chat-text')?.getAttribute('dir')).toBe('auto');
    });

    it('says the knowledge and agent chips in Arabic, each with its own tone', async () => {
      const { knowledgeStateWord, knowledgeStateTone } =
        await import('@app/features/knowledge/knowledge-words');
      const { agentStatusWord, agentStatusTone } =
        await import('@app/features/ai-agents/agent-words');

      expect(['Ready', 'NeedsIndex', 'Failed'].map(knowledgeStateWord)).toEqual([
        'مفهرس',
        'يحتاج إلى فهرسة',
        'فشلت الفهرسة',
      ]);
      expect(knowledgeStateTone('Failed')).toBe('danger');
      expect(agentStatusTone('Draft')).toBe('draft');
      expect(agentStatusWord('Draft')).toBe(CATALOGUE.translations['agents.status.draft']);
    });
  });

  describe('the inbox (Phase 38.2a)', () => {
    const customer = {
      id: 'p1',
      displayName: null,
      labelCode: 'AnonymousVisitor',
      reference: null,
      shortCode: '7KQ2MX',
    };

    async function label(over: object): Promise<HTMLElement> {
      const { CustomerLabel } = await import('@app/features/conversations/customer-label');
      const fixture = TestBed.createComponent(CustomerLabel);

      fixture.componentRef.setInput('customer', { ...customer, ...over });
      fixture.detectChanges();

      return fixture.nativeElement as HTMLElement;
    }

    it('names an unnamed customer in Arabic, with the code left to right', async () => {
      const visitor = await label({});

      expect(text(visitor)).toBe('زائر 7KQ2MX');
      expect(visitor.querySelector('bdi')?.getAttribute('dir')).toBe('ltr');
      expect(text(await label({ labelCode: 'WhatsAppCustomer', reference: '••••1234' }))).toBe(
        'عميل واتساب ••••1234',
      );
      expect(text(await label({ labelCode: 'UnnamedCustomer' }))).toBe('عميل 7KQ2MX');
    });

    it('writes the dividers and the start of the conversation in Arabic', async () => {
      const { ConversationTranscript } =
        await import('@app/features/conversations/conversation-transcript/conversation-transcript');
      const fixture = TestBed.createComponent(ConversationTranscript);
      const at = '2026-10-18T10:00:00Z';
      const actor = { userId: 'u1', email: 'sam@acme.test' };

      fixture.componentRef.setInput('conversationId', 'c1');
      fixture.componentRef.setInput('handling', 'Human');
      fixture.componentRef.setInput('messages', [
        { id: 'm1', sender: 'Customer', text: 'مرحبا', sentAt: at, sequence: 1, sentBy: null },
      ]);
      fixture.componentRef.setInput('events', [
        { id: 'e1', kind: 'AiTurnFailed', at, afterSequence: 1, actor: null, reason: 'x' },
        { id: 'e2', kind: 'TakenOver', at, afterSequence: 1, actor, reason: null },
        { id: 'e3', kind: 'ReturnedToAi', at, afterSequence: 1, actor, reason: null },
      ]);
      fixture.detectChanges();
      await fixture.whenStable();

      const root = fixture.nativeElement as HTMLElement;
      // The words of each line; its time follows them (39.4b).
      const event = (kind: string) =>
        text(
          root.querySelector(
            `[data-testid="transcript-event-${kind}"] .event-words > span:first-child`,
          ),
        );

      expect(event('AiTurnFailed')).toBe('تعذّر على الذكاء الاصطناعي الرد');
      expect(event('TakenOver')).toBe('استلام المحادثة: sam@acme.test');
      expect(event('ReturnedToAi')).toBe('إعادة المحادثة إلى الذكاء الاصطناعي: sam@acme.test');
      expect(text(root.querySelector('[data-testid="transcript-start"]'))).toBe('بداية المحادثة');
    });

    it('writes the thread in Arabic: a colleague by display name, the waiting line, no gendered verb (39.4b)', async () => {
      const { ConversationTranscript } =
        await import('@app/features/conversations/conversation-transcript/conversation-transcript');
      const fixture = TestBed.createComponent(ConversationTranscript);
      const at = '2026-10-18T10:00:00Z';
      const sara = { userId: 'u2', email: 'sara@acme.test', displayName: 'سارة' };

      fixture.componentRef.setInput('conversationId', 'c1');
      fixture.componentRef.setInput('handling', 'Ai');
      fixture.componentRef.setInput('messages', [
        { id: 'm1', sender: 'Customer', text: 'مرحبا', sentAt: at, sequence: 1, sentBy: null },
        { id: 'm2', sender: 'HumanAgent', text: 'أهلًا', sentAt: at, sequence: 2, sentBy: sara },
        { id: 'm3', sender: 'Customer', text: 'شكرًا', sentAt: at, sequence: 3, sentBy: null },
      ]);
      fixture.componentRef.setInput('events', [
        { id: 'e1', kind: 'TakenOver', at, afterSequence: 1, actor: sara, reason: null },
      ]);
      fixture.detectChanges();
      await fixture.whenStable();

      const root = fixture.nativeElement as HTMLElement;
      const author = root.querySelector('[data-testid="message-author"]');

      expect(text(author)).toBe('سارة');
      expect(author?.getAttribute('dir')).toBe('auto');
      expect(
        text(
          root.querySelector(
            '[data-testid="transcript-event-TakenOver"] .event-words > span:first-child',
          ),
        ),
      ).toBe('استلام المحادثة: سارة');
      expect(text(root.querySelector('[data-testid="awaiting-ai"]'))).toBe(
        'لم يردّ الذكاء الاصطناعي على الرسالة الأخيرة بعد.',
      );
      expect(root.textContent).not.toContain('تتولا');
    });

    it('writes the customer panel in Arabic, with the date in Western digits (39.4b)', async () => {
      const { ConversationContext } =
        await import('@app/features/conversations/conversation-context/conversation-context');
      const fixture = TestBed.createComponent(ConversationContext);

      fixture.componentRef.setInput('conversation', {
        id: 'c1',
        channel: 'WebChat',
        status: 'Open',
        handling: 'Ai',
        startedAt: '2026-09-07T10:00:00Z',
        lastActivityAt: '2026-09-07T10:05:00Z',
        agentName: 'المساعد',
        messageCount: 2,
        assignedTo: null,
        customer: { ...customer },
        lastReadSequence: null,
        unread: false,
        takenOverBy: null,
        needsPerson: false,
        needsPersonReason: null,
      });
      fixture.detectChanges();

      const root = fixture.nativeElement as HTMLElement;

      expect(text(root.querySelector('[data-testid="customer-channel"]'))).toBe('دردشة الموقع');
      expect(text(root.querySelector('[data-testid="context-assignee"]'))).toBe('لا أحد');
      expect(root.textContent).toContain('القناة');
      expect(root.textContent).not.toMatch(/[\u0660-\u0669]/);
    });

    it('writes the needs-a-person words in Arabic', async () => {
      const { customerLabelPlain } = await import('@app/features/conversations/customer-label');

      expect(customerLabelPlain(customer)).toBe('زائر 7KQ2MX');
      expect(CATALOGUE.translations['inbox.view.needsPerson']).toBe('بحاجة إلى الفريق');
      expect(CATALOGUE.translations['inbox.needsPerson.empty.heading']).toBe(
        'لا أحد بانتظار الفريق',
      );
      expect(CATALOGUE.translations['inbox.unread']).toBe('غير مقروءة');
      // The overview's gold tile (Phase 39.7a) uses the same words.
      expect(CATALOGUE.translations['overview.needsPerson.heading']).toBe('بحاجة إلى الفريق الآن');
      expect(CATALOGUE.translations['overview.needsPerson.more.two']).toBe(
        'ومحادثتان أخريان في صندوق المحادثات',
      );
      expect(CATALOGUE.translations['overview.needsPerson.more.many']).toBe(
        'و{$count} محادثة أخرى في صندوق المحادثات',
      );
      expect(CATALOGUE.translations['overview.attention.needsPerson.zero']).toBeDefined();
    });

    it('words the inbox refusals in Arabic, by their codes', async () => {
      const { CONVERSATION_PROBLEM_WORDS } =
        await import('@app/features/conversations/conversation-problems');

      expect(CONVERSATION_PROBLEM_WORDS['transcript.cursor.conflict']).toBe(
        'تعذّر تحميل هذه الرسائل. أعد تحميل الصفحة وحاول مرة أخرى.',
      );
      expect(CONVERSATION_PROBLEM_WORDS['whatsapp.message.required']).toBe(
        'اكتب رسالة قبل إرسالها.',
      );
    });
  });

  describe('the inbox list (Phase 39.4a)', () => {
    it('renders the list in Arabic: its heading, the three views, the chips and the dot', async () => {
      const { provideHttpClient } = await import('@angular/common/http');
      const { HttpTestingController, provideHttpClientTesting } =
        await import('@angular/common/http/testing');
      const { provideRouter } = await import('@angular/router');
      const { TenantApi } = await import('@app/core/api/tenant-api');
      const { PermissionApi } = await import('@app/core/authorization/permission-api');
      const { InboxSignals } = await import('@app/core/sounds/inbox-signals');
      const { LOCAL_STORAGE } = await import('@app/core/storage/local-storage');
      const { WorkspaceContextService } =
        await import('@app/core/workspace/workspace-context-service');
      const { memoryStorage, permissionApiGranting } = await import('@app/testing/test-doubles');
      const { ConversationsApi } = await import('@app/features/conversations/conversations-api');
      const { ConversationList } =
        await import('@app/features/conversations/conversation-list/conversation-list');
      const { signal } = await import('@angular/core');
      const tenantId = '11111111-1111-7111-8111-111111111111';

      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          ConversationsApi,
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          { provide: TenantApi, useValue: { get: () => undefined, create: () => undefined } },
          { provide: PermissionApi, useValue: permissionApiGranting('conversations.view') },
          { provide: InboxSignals, useValue: { count: signal(3), showCount: () => () => 0 } },
        ],
      });
      await TestBed.inject(WorkspaceContextService).adopt({
        id: tenantId,
        name: 'النور',
        slug: 'alnoor',
        status: 'Active',
      });

      const fixture = TestBed.createComponent(ConversationList);

      fixture.detectChanges();
      await fixture.whenStable();

      const conversation = {
        id: 'c1',
        channel: 'WhatsApp',
        status: 'Open',
        startedAt: '2026-11-14T10:00:00Z',
        lastActivityAt: '2026-11-14T10:05:00Z',
        agentName: null,
        messageCount: 2,
        lastMessagePreview: 'متى تفتحون؟',
        lastMessageSender: 'Customer',
        customer: {
          id: 'p1',
          displayName: null,
          labelCode: 'AnonymousVisitor',
          reference: null,
          shortCode: '7KQ2MX',
        },
        unread: true,
        assignedTo: null,
        needsPerson: true,
        needsPersonReason: 'PersonHandling',
      };

      TestBed.inject(HttpTestingController)
        .expectOne(`/api/tenants/${tenantId}/conversations?page=1&pageSize=20`)
        .flush({
          items: [
            {
              ...conversation,
              handling: 'Human',
              takenOverBy: { userId: 'u1', email: 'sara@acme.test', displayName: 'سارة' },
            },
            {
              ...conversation,
              id: 'c2',
              handling: 'Ai',
              unread: false,
              needsPerson: false,
              needsPersonReason: null,
              takenOverBy: null,
            },
          ],
          page: 1,
          pageSize: 20,
          total: 2,
        });
      await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();
      await fixture.whenStable();

      const root = fixture.nativeElement as HTMLElement;
      const rows = [...root.querySelectorAll('a.conversation-row')];

      expect(text(root.querySelector('h1'))).toBe('صندوق المحادثات');
      expect(text(root.querySelector('[data-testid="view-all"]'))).toBe('الكل');
      expect(text(root.querySelector('[data-testid="view-needs-person"]'))).toBe(
        'بحاجة إلى الفريق 3',
      );
      expect(text(root.querySelector('[data-testid="view-mine"]'))).toBe('محادثاتي');
      expect(text(root.querySelector('[data-testid="mark-all-read"]'))).toBe('تحديد الكل كمقروء');
      // «استلام المحادثة: …», never a gendered verb (Q8); the name in its own direction.
      const handled = rows[0].querySelector('[data-testid="row-handled-by"]');

      expect(text(handled)).toBe('استلام المحادثة: سارة');
      expect(handled?.querySelector('bdi')?.getAttribute('dir')).toBe('auto');
      expect(text(rows[0].querySelector('[data-testid="row-reason"]'))).toBe('بانتظار رد الفريق');
      expect(text(rows[0].querySelector('[data-testid="row-unread"]'))).toBe('غير مقروءة');
      expect(text(rows[1].querySelector('[data-testid="row-voice"]'))).toBe(
        'يردّ الذكاء الاصطناعي',
      );
      expect(rows[0].querySelector('.visually-hidden')?.textContent).toContain('واتساب');
      // The preview takes its own direction; the code stays left to right.
      expect(rows[0].querySelector('.row-text')?.getAttribute('dir')).toBe('auto');
      expect(rows[0].querySelector('app-customer-label bdi')?.getAttribute('dir')).toBe('ltr');
    });

    it('counts unread and marked conversations with every Arabic plural form', async () => {
      const { unreadSentence, markedSentence } =
        await import('@app/features/conversations/mark-all-read/mark-all-read');

      expect(unreadSentence('ar', 1)).toBe('في مساحة العمل هذه محادثة واحدة غير مقروءة.');
      expect(unreadSentence('ar', 2)).toBe('في مساحة العمل هذه محادثتان غيرُ مقروءتين.');
      expect(unreadSentence('ar', 5)).toBe('في مساحة العمل هذه 5 محادثات غير مقروءة.');
      expect(unreadSentence('ar', 961)).toBe('في مساحة العمل هذه 961 محادثة غير مقروءة.');
      expect(markedSentence('ar', 0)).toBe('كانت كل المحادثات مقروءة بالفعل.');
      expect(markedSentence('ar', 2)).toBe('حُدِّدت محادثتان كمقروءتين.');
      expect(markedSentence('ar', 11)).toBe('حُدِّدت 11 محادثة كمقروءة.');
    });

    it("gives the phone's bottom bar a one-line name for the inbox, and keeps the full one elsewhere", async () => {
      const { NAV_ITEMS } = await import('@app/layout/navigation');
      const inbox = NAV_ITEMS.find((item) => item.path === 'conversations');

      expect(inbox?.label).toBe('صندوق المحادثات');
      expect(inbox?.shortLabel).toBe('المحادثات');
      expect(CATALOGUE.translations['title.inbox']).toBe('صندوق المحادثات');
    });
  });

  describe('workspace, team, roles and billing (Phase 38.4)', () => {
    it('names the areas by key and counts role holders in Arabic', async () => {
      const { accessChangeSentence, areaName, memberCount, permissionLabel } =
        await import('@app/features/roles/permission-words');

      expect(areaName({ groupKey: 'members', group: 'Team members' })).toBe('أعضاء الفريق');
      expect(areaName({ groupKey: 'workspace', group: 'Workspace' })).toBe('مساحة العمل');
      // An area this build has no words for keeps the server's English.
      expect(areaName({ groupKey: 'integrations', group: 'Integrations' })).toBe('Integrations');
      expect(
        permissionLabel({
          code: 'workspace.manage',
          group: 'Workspace',
          groupKey: 'workspace',
          label: 'Manage workspace',
          description: '',
        }),
      ).toBe('إدارة مساحة العمل');

      expect(memberCount(0, 'ar')).toBe('لا أعضاء');
      expect(memberCount(1, 'ar')).toBe('عضو واحد');
      expect(memberCount(2, 'ar')).toBe('عضوان');
      expect(memberCount(3, 'ar')).toBe('3 أعضاء');
      expect(memberCount(11, 'ar')).toBe('11 عضوًا');
      expect(accessChangeSentence(3, 'ar')).toBe('سيغيّر الحفظ وصول 3 أعضاء.');
      expect(accessChangeSentence(0, 'ar')).toBe(
        'لا يحمل أي عضو هذا الدور بعد، لذا لن يغيّر الحفظ وصول أحد.',
      );
    });

    it('says each invitation list and its total in Arabic', async () => {
      const { invitationStatusWord, invitationSummary } =
        await import('@app/features/team/team-words');

      expect(invitationStatusWord('Pending')).toBe('بانتظار القبول');
      expect(invitationSummary('Pending', 2, 'ar')).toBe('دعوتان بانتظار القبول.');
      expect(invitationSummary('Accepted', 2, 'ar')).toBe('قُبلت دعوتان.');
      expect(invitationSummary('Revoked', 5, 'ar')).toBe('أُلغيت 5 دعوات.');
      expect(invitationSummary('Expired', 0, 'ar')).toBe('لم تنتهِ صلاحية أي دعوة.');
    });

    it('words the team, role, email-address and billing refusals in Arabic, by their codes', async () => {
      const { HttpErrorResponse } = await import('@angular/common/http');
      const { toApiError } = await import('@app/core/api/api-error');
      const { registerProblemWords } = await import('@app/core/problems/problem-words');
      const { TEAM_PROBLEM_WORDS } = await import('@app/features/team/team-problems');
      const { ROLE_PROBLEM_WORDS } = await import('@app/features/roles/role-problems');
      const { BILLING_PROBLEM_WORDS } = await import('@app/features/billing/billing-problems');

      [TEAM_PROBLEM_WORDS, ROLE_PROBLEM_WORDS, BILLING_PROBLEM_WORDS].forEach(registerProblemWords);

      const refuse = (code: string) =>
        toApiError(
          new HttpErrorResponse({
            status: 409,
            error: { title: 'Refused', detail: 'The English sentence.', code },
          }),
        ).message;

      expect(refuse('roles.name.taken')).toBe(
        'يحمل دور آخر في مساحة العمل هذه الاسم نفسه. اختر اسمًا مختلفًا.',
      );
      expect(refuse('invitations.email.already_invited')).toBe(
        'ما زالت دعوة لهذا العنوان بانتظار القبول. ألغِها أولًا لترسل دعوة جديدة.',
      );
      expect(refuse('email.invalid_characters')).toBe(
        'احذف المسافات أو الأحرف المخفية من عنوان البريد الإلكتروني.',
      );
      expect(refuse('entitlement.members.exhausted')).toContain('الحد الأقصى للأعضاء في باقتها');
      expect(refuse('billing.checkout.offernotavailable')).toBe(
        'سُحبت هذه الباقة من البيع. لم يُخصم أي مبلغ.',
      );
      // A code nobody registered gets the generic words, never the English detail.
      expect(refuse('roles.something.new')).not.toContain('The English sentence');
    });

    it('writes the workspace settings in Arabic for a member who reads them', async () => {
      const { provideHttpClient } = await import('@angular/common/http');
      const { HttpTestingController, provideHttpClientTesting } =
        await import('@angular/common/http/testing');
      const { provideRouter } = await import('@angular/router');
      const { PermissionApi } = await import('@app/core/authorization/permission-api');
      const { LOCAL_STORAGE } = await import('@app/core/storage/local-storage');
      const { WorkspaceContextService } =
        await import('@app/core/workspace/workspace-context-service');
      const { memoryStorage, permissionApiGranting } = await import('@app/testing/test-doubles');
      const { WorkspaceSettingsPage } =
        await import('@app/features/workspace-settings/workspace-settings-page/workspace-settings-page');

      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          { provide: PermissionApi, useValue: permissionApiGranting('workspace.view') },
        ],
      });

      const acme = {
        id: 't-1',
        name: 'شركة النور',
        slug: 'alnoor',
        status: 'Active' as const,
        timeZone: 'Asia/Riyadh',
      };

      await TestBed.inject(WorkspaceContextService).adopt(acme);

      const fixture = TestBed.createComponent(WorkspaceSettingsPage);

      fixture.detectChanges();
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tenants/t-1')
        .flush(acme, { headers: { ETag: '"v1"' } });
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();

      const root = fixture.nativeElement as HTMLElement;

      expect(text(root.querySelector('h1'))).toBe('إعدادات مساحة العمل');
      expect(text(root)).toContain('يرى الزوار هذا الاسم في صفحة الدردشة وفي نافذة الدردشة.');
      expect(text(root.querySelector('[data-testid="settings-read-only"]'))).toBe(
        'لا يغيّر هذه الإعدادات إلا من يستطيع إدارة مساحة العمل.',
      );
      expect(root.querySelector('[data-testid="workspace-name-text"]')?.getAttribute('dir')).toBe(
        'auto',
      );
      // The time zone (38.5): its heading in Arabic, its IANA id left to right.
      expect(text(root.querySelector('#settings-zone-heading'))).toBe('المنطقة الزمنية');
      expect(text(root.querySelector('[data-testid="time-zone-text"]'))).toBe('Asia/Riyadh');
      expect(root.querySelector('[data-testid="time-zone-text"]')?.getAttribute('dir')).toBe('ltr');
    });
  });

  describe('insights and reports (Phase 38.5)', () => {
    const LRI = '\u2066';
    const PDI = '\u2069';

    it('names the zone in Arabic, with the IANA id isolated', async () => {
      const { zonePhrase, zoneLabel } = await import('@app/core/i18n/time-zone');

      expect(zonePhrase('UTC')).toBe('بالتوقيت العالمي');
      expect(zonePhrase('Asia/Riyadh')).toBe(`بتوقيت ${LRI}Asia/Riyadh${PDI}`);
      expect(zoneLabel('UTC')).toBe('بالتوقيت العالمي');
    });

    it('says the period, the comparison and the data-since line in Arabic, with Western digits', async () => {
      const { periodSentence, comparisonSentence, dataSinceSentence, figureChange } =
        await import('@app/features/analytics/analytics-words');
      const dashboard = {
        period: {
          range: '7d',
          from: '2026-10-11T21:00:00Z',
          to: '2026-10-18T16:27:00Z',
          days: 7,
          timeZone: 'Asia/Riyadh',
        },
        dailyActivity: [{ date: '2026-10-12' }, { date: '2026-10-18' }],
        previous: {
          period: {
            range: '7d',
            from: '2026-10-04T21:00:00Z',
            to: '2026-10-11T21:00:00Z',
            days: 7,
            timeZone: 'Asia/Riyadh',
          },
        },
      } as unknown as import('@app/features/analytics/analytics-api').AnalyticsDashboard;

      const period = periodSentence(dashboard, 'ar');

      expect(period).toContain('أيام كاملة بتوقيت');
      expect(period).toContain('Asia/Riyadh');
      expect(period).toContain('يُحسب اليوم حتى');
      expect(period).not.toMatch(/[٠-٩]/);
      expect(comparisonSentence(dashboard, 'ar')).toMatch(
        /^مقارنةً بالفترة من .*5.*2026.* إلى .*11.*2026/,
      );
      expect(dataSinceSentence(null, 'ar')).toBe('لم تبدأ أي محادثة في مساحة العمل هذه بعد.');
      expect(dataSinceSentence('2026-03-03', 'ar')).toMatch(/^البيانات منذ 3 مارس 2026\.$/);
      // Arabic percentages carry invisible direction marks around the sign.
      expect(
        figureChange(12, 10, { soFar: true, earlierData: true }, 'ar').text.replace(
          /[\u200e\u200f\u061c]/g,
          '',
        ),
      ).toBe('حتى الآن، أعلى بنسبة 20% من الفترة السابقة');
      expect(figureChange(5, 0, { soFar: true, earlierData: false }, 'ar').text).toBe(
        'لا بيانات سابقة للمقارنة',
      );
    });

    it('names purposes, file languages and report options in Arabic', async () => {
      const { fileLanguageLabel, rangeLabel } = await import('@app/features/reports/reports-words');

      expect(fileLanguageLabel('ar')).toBe('ملف عربي');
      expect(fileLanguageLabel('en')).toBe('ملف إنجليزي');
      expect(rangeLabel('custom')).toBe('اختيار التواريخ');
    });

    it('names the report in a notification from its code, never from the English body', async () => {
      const { notificationText } = await import('@app/core/notifications/notification-words');
      const text = notificationText(
        {
          id: 'n1',
          kind: 'ReportCompleted',
          category: 'Reports',
          severity: 'Information',
          title: 'Your report is ready',
          body: 'Your “Channel usage” report has finished generating and is ready to download.',
          actionPath: '/reports',
          createdAt: '2026-10-19T09:00:00Z',
          readAt: null,
          parameters: { reportType: 'ChannelUsage' },
        },
        'ar',
      );

      expect(text.body).toContain('«استخدام القنوات»');
      expect(text.body).not.toContain('Channel usage');
    });

    it('words the time-zone refusal in Arabic', async () => {
      const { HttpErrorResponse } = await import('@angular/common/http');
      const { toApiError } = await import('@app/core/api/api-error');
      const { registerProblemWords } = await import('@app/core/problems/problem-words');
      const { WORKSPACE_PROBLEM_WORDS } =
        await import('@app/features/workspace-setup/workspace-problems');

      registerProblemWords(WORKSPACE_PROBLEM_WORDS);

      const error = toApiError(
        new HttpErrorResponse({
          status: 400,
          error: { code: 'workspace.time_zone.invalid', detail: 'Unknown time zone.' },
        }),
      );

      expect(error.message).toBe('لا تتعرّف نبرة على هذه المنطقة الزمنية. اختر واحدة من القائمة.');
    });
  });

  describe('the password-reset pages (Phase 37.3)', () => {
    async function render<T>(component: Type<T>): Promise<HTMLElement> {
      const { provideHttpClient } = await import('@angular/common/http');
      const { provideHttpClientTesting } = await import('@angular/common/http/testing');

      TestBed.configureTestingModule({
        providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
      });
      const fixture = TestBed.createComponent(component);
      fixture.detectChanges();
      await fixture.whenStable();

      return fixture.nativeElement as HTMLElement;
    }

    afterEach(() => window.history.replaceState(null, '', '/'));

    it('offers the way to a new password on the sign-in page', async () => {
      const { Login } = await import('@app/features/auth/login/login');
      const root = await render(Login);

      expect(text(root.querySelector('a[href="/forgot-password"]'))).toBe('نسيت كلمة المرور؟');
    });

    it('asks for the address in Arabic, with the address itself left to right', async () => {
      const { ForgotPassword } = await import('@app/features/auth/forgot-password/forgot-password');
      const root = await render(ForgotPassword);

      expect(text(root.querySelector('h1'))).toBe('إعادة تعيين كلمة المرور');
      expect(text(root.querySelector('label[for="email"]'))).toBe('البريد الإلكتروني');
      expect(root.querySelector('#email')?.getAttribute('dir')).toBe('ltr');
      expect(text(root.querySelector('button[type="submit"]'))).toBe('إرسال رابط إعادة التعيين');
    });

    it('sets a new password in Arabic, with the policy in Western digits', async () => {
      window.history.replaceState(null, '', '/reset-password#uid=u-1&token=t-1');
      const { ResetPassword } = await import('@app/features/auth/reset-password/reset-password');
      const root = await render(ResetPassword);

      expect(text(root.querySelector('h1'))).toBe('تعيين كلمة مرور جديدة');
      expect(text(root.querySelector('label[for="password"]'))).toBe('كلمة المرور الجديدة');
      expect(text(root.querySelector('label[for="confirmation"]'))).toBe(
        'تأكيد كلمة المرور الجديدة',
      );
      expect(text(root.querySelector('#password-hint'))).toBe(
        '12 حرفًا على الأقل. العبارة التي يسهل تذكّرها خيار جيد.',
      );
    });

    it('says in Arabic that a link without its parts is not valid', async () => {
      window.history.replaceState(null, '', '/reset-password');
      const { ResetPassword } = await import('@app/features/auth/reset-password/reset-password');
      const root = await render(ResetPassword);

      expect(text(root.querySelector('h1'))).toBe('هذا الرابط غير صالح');
      expect(text(root.querySelector('a[href="/forgot-password"]'))).toBe('طلب رابط جديد');
    });
  });

  describe("the account menu's appearance (Phase 39.2a)", () => {
    it('offers «تقليل الشفافية» beside the theme, as a checkbox item that paints the setting', async () => {
      const { LOCAL_STORAGE } = await import('@app/core/storage/local-storage');
      const { MEDIA_QUERY_FACTORY, MediaQueries } = await import('@app/core/media/media');
      const { FakeMediaMatcher, memoryStorage } = await import('@app/testing/test-doubles');
      const { ProfileMenu } = await import('@app/layout/profile-menu/profile-menu');
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          {
            provide: MEDIA_QUERY_FACTORY,
            useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
          },
        ],
      });
      const fixture = TestBed.createComponent(ProfileMenu);
      fixture.componentRef.setInput('email', 'ada.owner@omniai.example');
      document.body.appendChild(fixture.nativeElement);
      fixture.detectChanges();
      await fixture.whenStable();
      const root = fixture.nativeElement as HTMLElement;
      root.querySelector<HTMLButtonElement>('[aria-haspopup="menu"]')!.click();
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();

      const item = root.querySelector<HTMLButtonElement>('[data-testid="reduce-transparency"]')!;
      expect(text(item)).toBe('تقليل الشفافية');
      expect(text(root.querySelector('#profile-theme-label'))).toBe('المظهر');
      expect(item.getAttribute('role')).toBe('menuitemcheckbox');

      expect(item.getAttribute('aria-checked')).toBe('false');
      item.click();
      fixture.detectChanges();
      TestBed.tick();

      expect(document.documentElement.getAttribute('data-transparency')).toBe('reduced');

      document.documentElement.removeAttribute('data-transparency');
      root.remove();
    });
  });

  describe('customers, team, roles and the display name (Phase 39.6a)', () => {
    it('labels an unnamed WhatsApp customer by its masked number, left to right', async () => {
      const { asLabelled } = await import('@app/features/customers/customer-words');
      const { customerLabelPlain } = await import('@app/features/conversations/customer-label');

      const person = {
        id: 'a',
        displayName: null,
        labelCode: 'WhatsAppCustomer',
        shortCode: 'W4T5PQ',
        reference: '••••4821',
      };

      expect(customerLabelPlain(asLabelled(person))).toBe('عميل واتساب ••••4821');
      expect(customerLabelPlain(asLabelled({ ...person, reference: null }))).toBe(
        'عميل واتساب W4T5PQ',
      );
    });

    it('wears the invitation states as chips with Arabic words', async () => {
      const { invitationStatusTone, invitationStatusWord } =
        await import('@app/features/team/team-words');

      expect(invitationStatusWord('Expired')).not.toBe('Expired');
      expect(invitationStatusTone('Pending')).toBe('info');
      expect(invitationStatusTone('Accepted')).toBe('success');
      expect(invitationStatusTone('Expired')).toBe('draft');
      expect(invitationStatusTone('Revoked')).toBe('neutral');
    });

    it('says the sounds state with the plainer pair «تعمل» / «متوقفة»', async () => {
      const { LOCAL_STORAGE } = await import('@app/core/storage/local-storage');
      const { MEDIA_QUERY_FACTORY, MediaQueries } = await import('@app/core/media/media');
      const { FakeMediaMatcher, memoryStorage, permissionApiGranting } =
        await import('@app/testing/test-doubles');
      const { PermissionApi } = await import('@app/core/authorization/permission-api');
      const { PermissionService } = await import('@app/core/authorization/permission-service');
      const { SoundSettings } = await import('@app/core/sounds/sound-settings');
      const { ProfileMenu } = await import('@app/layout/profile-menu/profile-menu');
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          { provide: PermissionApi, useValue: permissionApiGranting('conversations.view') },
          {
            provide: MEDIA_QUERY_FACTORY,
            useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
          },
        ],
      });
      const fixture = TestBed.createComponent(ProfileMenu);
      fixture.componentRef.setInput('email', 'ada.owner@omniai.example');
      fixture.componentRef.setInput('variant', 'member');
      fixture.componentRef.setInput('displayName', 'سارة أحمد');
      await TestBed.inject(PermissionService).load('t1');
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(text(root.querySelector('.member-status'))).toBe('الأصوات تعمل');
      expect(text(root.querySelector('.member-name'))).toBe('سارة أحمد');

      TestBed.inject(SoundSettings).setEnabled(false);
      fixture.detectChanges();

      expect(text(root.querySelector('.member-status'))).toBe('الأصوات متوقفة');
    });

    it('writes the display-name dialog and its refusals in Arabic, with Western digits', async () => {
      const { AuthService } = await import('@app/core/auth/auth-service');
      const { ToastService } = await import('@app/core/feedback/toast-service');
      const { LanguageService } = await import('@app/core/i18n/language-service');
      const { stubDialogs } = await import('@app/testing/test-doubles');
      const { DisplayNameDialog } = await import('@app/layout/profile-menu/display-name-dialog');
      const { displayNameTooLong, DISPLAY_NAME_CONTROL_CHARACTERS } =
        await import('@app/layout/profile-menu/display-name-problems');
      TestBed.configureTestingModule({
        providers: [
          { provide: AuthService, useValue: { saveDisplayName: () => Promise.resolve() } },
          { provide: ToastService, useValue: { success: () => undefined } },
          { provide: LanguageService, useValue: { current: 'ar' } },
        ],
      });
      const fixture = TestBed.createComponent(DisplayNameDialog);
      fixture.detectChanges();
      stubDialogs(fixture.nativeElement);
      fixture.componentRef.setInput('requests', 1);
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(text(root.querySelector('#display-name-title'))).toBe('اسمك الظاهر');
      expect(text(root.querySelector('#display-name-lede'))).toBe(
        'اسمك كما يراه فريقك. لا يراه العملاء، فهم يرون «فريق الدعم» بدلًا منه.',
      );
      expect(text(root.querySelector('label[for="display-name-input"]'))).toBe('الاسم الظاهر');
      expect(text(root.querySelector('[data-testid="save-display-name"]'))).toBe('حفظ الاسم');

      // 80 takes the "many" form, in Western digits.
      expect(displayNameTooLong('ar')).toBe(
        'يمكن أن يتكوّن اسمك الظاهر من 80 حرفًا على الأكثر. اختصره ثم احفظ مرة أخرى.',
      );
      expect(displayNameTooLong('ar', 2)).toBe(
        'يمكن أن يتكوّن اسمك الظاهر من حرفين على الأكثر. اختصره ثم احفظ مرة أخرى.',
      );
      expect(DISPLAY_NAME_CONTROL_CHARACTERS).toBe(
        'يحوي اسمك الظاهر رمزًا مخفيًا، مثل علامة اتجاه. احذفه ثم احفظ مرة أخرى.',
      );
    });
  });

  describe('plan and usage, billing, settings and notifications (Phase 39.6b)', () => {
    async function quota(entitlement: Record<string, unknown>): Promise<HTMLElement> {
      const { QuotaRow } = await import('@app/features/plan-usage/quota-row/quota-row');
      const fixture = TestBed.createComponent(QuotaRow);

      fixture.componentRef.setInput('entitlement', entitlement);
      fixture.detectChanges();
      await fixture.whenStable();

      return fixture.nativeElement as HTMLElement;
    }

    it('says a limit in Arabic: the state as a chip, the count raised in its own sentence', async () => {
      const root = await quota({
        entitlement: 'members',
        used: 4,
        limit: 5,
        unlimited: false,
        percentUsed: 80,
        state: 'Warning',
        remaining: 1,
        period: null,
      });

      const chip = root.querySelector('[data-testid="state-members"]')!;

      expect(text(chip)).toBe('يقترب من الحد');
      expect(chip.classList).toContain('chip-warning');
      expect(text(root.querySelector('[data-testid="figure-members"]'))).toBe('4 من 5');
      expect(text(root.querySelector('.quota-used'))).toBe('4');
      expect(root.querySelector('app-meter')?.getAttribute('aria-valuetext')).toBe(
        '4 من 5. يقترب من الحد',
      );
    });

    it('raises the count where the Arabic sentence puts it last (unlimited)', async () => {
      const root = await quota({
        entitlement: 'aiAgents',
        used: 11,
        limit: null,
        unlimited: true,
        percentUsed: null,
        state: 'Normal',
        remaining: null,
        period: null,
      });

      expect(text(root.querySelector('[data-testid="figure-aiAgents"]'))).toBe('قيد الاستخدام: 11');
      expect(text(root.querySelector('.quota-used'))).toBe('11');
      expect(text(root.querySelector('[data-testid="state-aiAgents"]'))).toBe('غير محدود');
      expect(root.querySelector('app-meter')).toBeNull();
    });

    it('names the zone in use in Arabic beside its IANA id', async () => {
      const { provideHttpClient } = await import('@angular/common/http');
      const { HttpTestingController, provideHttpClientTesting } =
        await import('@angular/common/http/testing');
      const { PermissionApi } = await import('@app/core/authorization/permission-api');
      const { LOCAL_STORAGE } = await import('@app/core/storage/local-storage');
      const { WorkspaceContextService } =
        await import('@app/core/workspace/workspace-context-service');
      const { memoryStorage, permissionApiGranting } = await import('@app/testing/test-doubles');
      const { WorkspaceSettingsPage } =
        await import('@app/features/workspace-settings/workspace-settings-page/workspace-settings-page');

      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          provideHttpClient(),
          provideHttpClientTesting(),
          { provide: LOCAL_STORAGE, useValue: memoryStorage() },
          {
            provide: PermissionApi,
            useValue: permissionApiGranting('workspace.view', 'workspace.manage'),
          },
        ],
      });

      const workspace = {
        id: 't-1',
        name: 'شركة النور',
        slug: 'alnoor',
        status: 'Active' as const,
        timeZone: 'Asia/Riyadh',
      };

      await TestBed.inject(WorkspaceContextService).adopt(workspace);

      const fixture = TestBed.createComponent(WorkspaceSettingsPage);

      fixture.detectChanges();
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tenants/t-1')
        .flush({ ...workspace, scheduleCount: 2 }, { headers: { ETag: '"v1"' } });
      await fixture.whenStable();
      await new Promise((resolve) => setTimeout(resolve, 0));
      fixture.detectChanges();

      const root = fixture.nativeElement as HTMLElement;
      const tile = root.querySelector('[data-testid="zone-current"]');

      expect(text(tile?.querySelector('dt'))).toBe('المنطقة الزمنية المستخدمة');
      expect(text(root.querySelector('[data-testid="zone-name"]'))).toBe('التوقيت العربي الرسمي');
      expect(text(root.querySelector('[data-testid="zone-id"]'))).toBe('Asia/Riyadh');
      expect(root.querySelector('[data-testid="zone-id"]')?.getAttribute('dir')).toBe('ltr');
      expect(text(root.querySelector('[data-testid="schedule-count"] dt'))).toBe(
        'التقارير المجدولة وفق هذه المنطقة',
      );
      expect(text(root.querySelector('[data-testid="schedule-count"] dd'))).toBe('2');
    });

    it('says the billing and email states in Arabic chips', async () => {
      const { subscriptionStatus } = await import('@app/features/billing/billing-words');

      expect(subscriptionStatus('Active').chip).toBe('chip-success');
      expect(subscriptionStatus('PastDue').chip).toBe('chip-warning');
      expect(subscriptionStatus('Suspended').chip).toBe('chip-danger');
      expect(subscriptionStatus('PendingActivation').chip).toBe('chip-draft');
      expect(subscriptionStatus('Active').word).not.toBe('Active');
      expect(CATALOGUE.translations['billing.state.notSetUp']).toBe('المدفوعات غير مُعدّة');
      expect(CATALOGUE.translations['notificationPreferences.emailNotSetUp']).toBe(
        'البريد الإلكتروني غير مُعدّ',
      );
      expect(CATALOGUE.translations['workspaceSettings.zone.selected']).toBe('المنطقة المختارة:');
    });
  });

  describe('the 38.7 contracts (Phase 38.8)', () => {
    async function refuse(
      catalogue: Record<string, unknown>,
      code: string,
      status = 400,
      extra: object = {},
    ): Promise<string> {
      const { HttpErrorResponse } = await import('@angular/common/http');
      const { toApiError } = await import('@app/core/api/api-error');
      const { registerProblemWords } = await import('@app/core/problems/problem-words');

      registerProblemWords(catalogue as never);

      return toApiError(
        new HttpErrorResponse({
          status,
          error: { title: 'Refused', detail: 'The English sentence.', code, ...extra },
        }),
      ).message;
    }

    it('says why a conversation needs a person, and who took it over, in Arabic', async () => {
      const { needsPersonWord } = await import('@app/features/conversations/conversation-words');

      expect(needsPersonWord('PersonHandling')).toBe('بانتظار رد الفريق');
      expect(needsPersonWord('AiTurnFailed')).toBe('تعذّر على الذكاء الاصطناعي الرد');
      expect(needsPersonWord('ReturnedUnanswered')).toBe(
        'تمّت إعادتها إلى الذكاء الاصطناعي دون رد',
      );
      expect(needsPersonWord('SomethingNewer')).toBe('بانتظار رد الفريق');
      expect(CATALOGUE.translations['conversation.handledBy']).toBe(
        'تمّ استلام المحادثة من قِبل {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI}',
      );
      expect(CATALOGUE.translations['inbox.handledBy']).toBe(
        'استلام المحادثة: {$START_TAG_BDI}{$INTERPOLATION}{$CLOSE_TAG_BDI}',
      );
    });

    it('names a directory customer as the inbox does, in Arabic (BA-1)', async () => {
      const { asLabelled } = await import('@app/features/customers/customer-words');
      const { customerLabelPlain } = await import('@app/features/conversations/customer-label');
      const record = { id: 'p1', displayName: null, shortCode: '7KQ2MX' };

      expect(customerLabelPlain(asLabelled({ ...record, labelCode: 'AnonymousVisitor' }))).toBe(
        'زائر 7KQ2MX',
      );
      expect(customerLabelPlain(asLabelled({ ...record, labelCode: 'WhatsAppCustomer' }))).toBe(
        'عميل واتساب 7KQ2MX',
      );
      expect(customerLabelPlain(asLabelled({ ...record, labelCode: 'UnnamedCustomer' }))).toBe(
        'عميل 7KQ2MX',
      );
    });

    it('says indexing carries on and ends here, and a timed-out attempt, in Arabic', async () => {
      const { indexFailureReason } = await import('@app/features/knowledge/knowledge-words');

      expect(CATALOGUE.translations['knowledge.state.indexing.live']).toBe(
        'تجري الفهرسة الآن. تستمر حتى لو غادرت هذه الصفحة، وتظهر النتيجة هنا عند انتهائها.',
      );
      expect(indexFailureReason('timed_out')).toBe(
        'استغرقت الفهرسة وقتًا أطول من المسموح به، فلم يُفهرَس شيء.',
      );
    });

    it('counts the files inside a Word document with every Arabic plural form (BA-12)', async () => {
      const { KNOWLEDGE_PROBLEM_WORDS } =
        await import('@app/features/knowledge/knowledge-problems');
      const archive = (limit: number, limitUnit: string) =>
        refuse(KNOWLEDGE_PROBLEM_WORDS, 'knowledge.file.archive_too_large', 422, {
          limit,
          limitUnit,
        });

      expect(await archive(1, 'entries')).toContain('على أكثر من ملف واحد.');
      expect(await archive(2, 'entries')).toContain('على أكثر من ملفين.');
      expect(await archive(3, 'entries')).toContain('على أكثر من 3 ملفات.');
      expect(await archive(512, 'entries')).toContain('على أكثر من 512 ملفًا.');
      expect(await archive(500, 'entries')).toContain('على أكثر من 500 ملف.');
      expect(await archive(67108864, 'bytes')).toContain('يتجاوز حجم مستند Word هذا عند فتحه');
      expect(await archive(67108864, 'bytes')).toContain('64');
    });

    it('says how many scheduled reports move, with every Arabic plural form (BA-9)', async () => {
      const { schedulesMove } =
        await import('@app/features/workspace-settings/workspace-settings-page/workspace-settings-page');

      expect(schedulesMove('ar', 1)).toMatch(/^ينتقل تقرير مجدول واحد إلى/);
      expect(schedulesMove('ar', 2)).toMatch(/^ينتقل تقريران مجدولان إلى/);
      expect(schedulesMove('ar', 3)).toMatch(/^تنتقل 3 تقارير مجدولة إلى/);
      expect(schedulesMove('ar', 12)).toMatch(/^ينتقل 12 تقريرًا مجدولًا إلى/);
      expect(schedulesMove('ar', 100)).toMatch(/^ينتقل 100 تقرير مجدول إلى/);
    });

    it('words the codes 38.7b added in Arabic, by their codes (BA-4)', async () => {
      const { CONVERSATION_PROBLEM_WORDS } =
        await import('@app/features/conversations/conversation-problems');
      const { CUSTOMER_PROBLEM_WORDS } = await import('@app/features/customers/customer-problems');
      const { NOTIFICATION_PROBLEM_WORDS } =
        await import('@app/features/notifications/notification-problems');
      const { REPORT_PROBLEM_WORDS } = await import('@app/features/reports/report-problems');

      expect(
        await refuse(CONVERSATION_PROBLEM_WORDS, 'conversations.reply.not_taken_over', 409),
      ).toBe('يردّ الذكاء الاصطناعي على هذه المحادثة. اختر «استلام المحادثة» أولًا، ثم ردّ.');
      expect(await refuse(CUSTOMER_PROBLEM_WORDS, 'customers.merge.self')).toBe(
        'لا يمكن دمج عميل مع نفسه. اختر عميلًا آخر.',
      );
      expect(await refuse(NOTIFICATION_PROBLEM_WORDS, 'notifications.page.invalid')).toBe(
        'تعذّر عرض هذه الصفحة من الإشعارات. عد إلى الصفحة الأولى.',
      );
      expect(await refuse(REPORT_PROBLEM_WORDS, 'reports.schedule.limit_reached', 409)).toBe(
        'بلغت مساحة العمل هذه أقصى عدد مسموح به من الجدولات. احذف واحدة قبل إضافة أخرى.',
      );
      expect(await refuse(REPORT_PROBLEM_WORDS, 'reports.file.expired', 410)).toBe(
        'لم يعد ملف هذا التقرير محفوظًا. أنشئ التقرير مرة أخرى.',
      );

      // Never the server's English, for any code of the four areas.
      for (const [catalogue, codes] of [
        [CONVERSATION_PROBLEM_WORDS, Object.keys(CONVERSATION_PROBLEM_WORDS)],
        [CUSTOMER_PROBLEM_WORDS, Object.keys(CUSTOMER_PROBLEM_WORDS)],
        [NOTIFICATION_PROBLEM_WORDS, Object.keys(NOTIFICATION_PROBLEM_WORDS)],
        [REPORT_PROBLEM_WORDS, Object.keys(REPORT_PROBLEM_WORDS)],
      ] as const) {
        for (const code of codes) {
          const said = await refuse(catalogue, code);

          expect(said).not.toContain('The English sentence');
          expect(said).toMatch(/[؀-ۿ]/);
        }
      }
    });
  });
});
