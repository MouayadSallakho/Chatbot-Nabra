import { Type } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import type { ConversationSummary } from '@app/features/conversations/conversations-api';
import { AnsweredTile } from './answered-tile';
import { AttentionTile } from './attention-tile';
import { ChannelsTile } from './channels-tile';
import { NeedsPersonTile } from './needs-person-tile';
import { UsageTile } from './usage-tile';

/**
 * The bento's tiles on their own (Phase 39.7a): each state (loading, empty,
 * error, data) and both languages. The overview spec covers them in place.
 */
describe('overview tiles', () => {
  function mount<T>(
    component: Type<T>,
    inputs: Record<string, unknown>,
    language: AppLanguage = 'en',
  ): ComponentFixture<T> {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: APP_LANGUAGE, useValue: language }],
    });

    const fixture = TestBed.createComponent(component);

    for (const [name, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(name, value);
    }

    fixture.detectChanges();

    return fixture;
  }

  const el = (fixture: ComponentFixture<unknown>): HTMLElement =>
    fixture.nativeElement as HTMLElement;

  afterEach(() => document.documentElement.removeAttribute('dir'));

  describe('Answered by the AI', () => {
    it('draws the share as a ring and says it in words', () => {
      const fixture = mount(AnsweredTile, {
        answered: 25,
        started: 40,
        previousAnswered: 32,
        previousStarted: 50,
        earlierData: true,
      });
      const host = el(fixture);

      expect(host.querySelector('[data-testid="answered-share"]')?.textContent?.trim()).toBe('63%');
      expect(host.querySelector('.ring svg')?.getAttribute('aria-hidden')).toBe('true');
      // 25 of 40 of a 314.16 circle.
      expect(host.querySelector('.ring-arc')?.getAttribute('stroke-dasharray')).toBe(
        '196.35 314.16',
      );
      expect(host.querySelector('[data-testid="answered-before"]')?.textContent?.trim()).toBe(
        'The 7 days before: 64%',
      );
    });

    it('in Arabic keeps Latin digits and mirrors only the drawing', () => {
      document.documentElement.setAttribute('dir', 'rtl');
      const fixture = mount(AnsweredTile, { answered: 25, started: 40 }, 'ar');
      const host = el(fixture);
      const share = host.querySelector('[data-testid="answered-share"]')?.textContent ?? '';

      expect(share).toContain('63');
      expect(share).not.toMatch(/[٠-٩]/);
      expect(host.querySelector('[data-testid="answered-counts"]')?.textContent?.trim()).toBe(
        '25 of 40',
      );
    });

    it('draws no arc and says so when no conversation started', () => {
      const host = el(mount(AnsweredTile, { answered: 0, started: 0 }));

      expect(host.querySelector('.ring-arc')).toBeNull();
      expect(host.querySelector('[data-testid="answered-share"]')).toBeNull();
      expect(host.querySelector('[data-testid="answered-none"]')?.textContent?.trim()).toBe(
        'No conversation started in the last 7 days.',
      );
    });

    it('holds the ring and two lines while loading', () => {
      const host = el(mount(AnsweredTile, { loading: true }));

      expect(host.querySelector('.ring-skeleton')).not.toBeNull();
      expect(host.querySelectorAll('.answered-skeleton').length).toBe(2);
    });
  });

  describe('Needs a person now', () => {
    const row = (id: string, reason: string): ConversationSummary =>
      ({
        id,
        channel: 'WhatsApp',
        handling: 'Human',
        lastMessageSender: 'Customer',
        takenOverBy: null,
        assignedTo: null,
        needsPerson: true,
        needsPersonReason: reason,
        customer: {
          id: `c-${id}`,
          displayName: null,
          labelCode: 'WhatsAppCustomer',
          reference: '••1234',
          shortCode: 'W1',
        },
      }) as unknown as ConversationSummary;

    it('names an unnamed WhatsApp customer by the masked number, left to right', () => {
      const host = el(
        mount(NeedsPersonTile, { conversations: [row('a', 'PersonHandling')], total: 1 }),
      );
      const code = host.querySelector('app-customer-label bdi');

      expect(host.querySelector('app-customer-label')?.textContent).toContain('WhatsApp customer');
      expect(code?.textContent).toBe('••1234');
      expect(code?.getAttribute('dir')).toBe('ltr');
      expect(host.querySelector('[data-testid="needs-person-more"]')).toBeNull();
    });

    it('says how many more wait, in the plural form the language needs', () => {
      const conversations = [row('a', 'AiTurnFailed'), row('b', 'AiTurnFailed'), row('c', 'x')];
      const english = el(mount(NeedsPersonTile, { conversations, total: 4 }));

      expect(english.querySelector('[data-testid="needs-person-more"]')?.textContent?.trim()).toBe(
        'and 1 more in the inbox',
      );
      TestBed.resetTestingModule();

      const arabic = el(mount(NeedsPersonTile, { conversations, total: 152 }, 'ar'));

      // Arabic picks the "many" form for 149; the number keeps Latin digits.
      expect(arabic.querySelector('[data-testid="needs-person-more"]')?.textContent).toContain(
        '149',
      );
      expect(arabic.querySelector('[data-testid="needs-person-total"]')?.textContent?.trim()).toBe(
        '152',
      );
    });

    it('offers the one action in every state', () => {
      for (const inputs of [{ loading: true }, { failed: true }, { total: 0 }]) {
        TestBed.resetTestingModule();
        const host = el(mount(NeedsPersonTile, inputs));

        expect(host.querySelector('[data-testid="needs-person-open"]')?.textContent?.trim()).toBe(
          'Open the inbox',
        );
      }
    });
  });

  describe('Channels', () => {
    it('lists each channel with its share and its conversations, and hides the bar', () => {
      const host = el(
        mount(ChannelsTile, {
          channels: [
            { channel: 'WebChat', conversationsStarted: 1 },
            { channel: 'WhatsApp', conversationsStarted: 3 },
          ],
        }),
      );

      expect(host.querySelector('.split')?.getAttribute('aria-hidden')).toBe('true');
      expect(
        [...host.querySelectorAll('.legend li')].map((li) =>
          [...li.querySelectorAll('span:not([aria-hidden])')].map((part) => part.textContent),
        ),
      ).toEqual([
        ['WhatsApp', '75%', '3 conversations'],
        ['Website chat', '25%', '1 conversation'],
      ]);
    });

    it('says when no conversation started, and draws nothing', () => {
      const host = el(mount(ChannelsTile, { channels: [] }));

      expect(host.querySelector('.split')).toBeNull();
      expect(host.querySelector('[data-testid="channels-none"]')).not.toBeNull();
    });
  });

  describe('AI usage this month', () => {
    const usage = (state: string, used: number, limit: number | null) => ({
      entitlement: 'monthlyProviderOperations',
      used,
      limit,
      unlimited: limit === null,
      percentUsed: null,
      remaining: null,
      state,
      period: null,
    });

    it('says a reached limit as a chip with its word, beside the meter', () => {
      const host = el(mount(UsageTile, { entitlement: usage('Exhausted', 200, 200) }));

      expect(host.querySelector('[data-testid="usage-state"]')?.textContent?.trim()).toBe(
        'Limit reached',
      );
      expect(host.querySelector('app-meter')?.classList.contains('is-exhausted')).toBe(true);
    });

    it('formats the figures in Arabic with Latin digits', () => {
      const host = el(mount(UsageTile, { entitlement: usage('Normal', 1234, 5000) }, 'ar'));

      expect(host.querySelector('[data-testid="usage-used"]')?.textContent?.trim()).toBe('1,234');
    });

    it('offers to try again when the plan could not be read', () => {
      const fixture = mount(UsageTile, { failed: true });
      let retried = 0;

      fixture.componentInstance.retry.subscribe(() => (retried += 1));
      el(fixture).querySelector<HTMLButtonElement>('.usage-retry')!.click();

      expect(retried).toBe(1);
      expect(el(fixture).querySelector('[data-testid="usage-limit"]')).toBeNull();
    });
  });

  describe('Needs attention', () => {
    it('says "All clear" with a word and a glyph, never by colour alone', () => {
      const host = el(mount(AttentionTile, { items: [] }));

      expect(
        host.querySelector('[data-testid="attention-clear"] app-chip')?.textContent?.trim(),
      ).toBe('All clear');
    });

    it('gives each row its title, then its severity chip and detail', () => {
      const host = el(
        mount(AttentionTile, {
          items: [
            {
              id: 'billing',
              tone: 'critical',
              title: 'Subscription suspended',
              detail: 'Payment has not been received.',
              link: '/billing',
              action: 'Open billing',
            },
          ],
        }),
      );
      const row = host.querySelector('[data-testid="attention-billing"]')!;

      expect(row.querySelector('a')?.getAttribute('href')).toBe('/billing');
      expect(row.querySelector('.attention-title')?.textContent?.trim()).toBe(
        'Subscription suspended',
      );
      expect(row.querySelector('.attention-meta app-chip')?.getAttribute('data-tone')).toBe(
        'danger',
      );
      expect(row.querySelector('.attention-detail')?.textContent?.trim()).toBe(
        'Payment has not been received.',
      );
    });
  });
});
