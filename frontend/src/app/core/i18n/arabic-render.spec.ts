// @ts-expect-error No Node type declarations are installed in this workspace.
import { readFileSync } from 'node:fs';
import { LOCALE_ID, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { registerLocaleData } from '@angular/common';
import localeAr from '@angular/common/locales/ar';
import { clearTranslations, loadTranslations } from '@angular/localize';

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

  it('counts unread notifications with Arabic plurals', async () => {
    const { unreadText } = await import('@app/core/notifications/notification-center');

    expect(unreadText(0, 'ar')).toBe('لا توجد إشعارات غير مقروءة');
    expect(unreadText(2, 'ar')).toBe('إشعاران غير مقروءين');
    expect(unreadText(7, 'ar')).toBe('7 إشعارات غير مقروءة');
    expect(unreadText(57, 'ar')).toBe('57 إشعارًا غير مقروء');
  });
});
