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
    expect(text(root.querySelector('#entry-story-title'))).toBe('صوتان… بنبرة واحدة');
    expect(text(root.querySelector('figcaption'))).toBe('محادثة توضيحية');
    expect(text(root.querySelector('.bubble-ai .bubble-speaker'))).toBe('الذكاء الاصطناعي');
    expect(text(root.querySelector('.handoff-divider'))).toBe('استلم أحد أعضاء الفريق المحادثة');
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
      const event = (kind: string) =>
        text(root.querySelector(`[data-testid="transcript-event-${kind}"]`));

      expect(event('AiTurnFailed')).toBe('تعذّر على الذكاء الاصطناعي الرد');
      expect(event('TakenOver')).toBe('استلام المحادثة: sam@acme.test');
      expect(event('ReturnedToAi')).toBe('إعادة المحادثة إلى الذكاء الاصطناعي: sam@acme.test');
      expect(text(root.querySelector('[data-testid="transcript-start"]'))).toBe('بداية المحادثة');
    });

    it('writes the needs-a-person words in Arabic', async () => {
      const { customerLabelPlain } = await import('@app/features/conversations/customer-label');

      expect(customerLabelPlain(customer)).toBe('زائر 7KQ2MX');
      expect(CATALOGUE.translations['inbox.view.needsPerson']).toBe('بحاجة إلى الفريق');
      expect(CATALOGUE.translations['inbox.needsPerson.empty.heading']).toBe(
        'لا أحد بانتظار الفريق',
      );
      expect(CATALOGUE.translations['inbox.unread']).toBe('غير مقروءة');
      expect(CATALOGUE.translations['overview.attention.needsPerson.two']).toBe(
        'محادثتان بحاجة إلى الفريق',
      );
      expect(CATALOGUE.translations['overview.attention.needsPerson.many']).toBe(
        '{$count} محادثة بحاجة إلى الفريق',
      );
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

      const acme = { id: 't-1', name: 'شركة النور', slug: 'alnoor', status: 'Active' as const };

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
});
