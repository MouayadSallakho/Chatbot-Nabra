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
