import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { APP_LANGUAGE, LANGUAGE_STORAGE_KEY } from '@app/core/i18n/language';
import { PAGE_RELOAD } from '@app/core/i18n/language-service';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { memoryStorage } from '@app/testing/test-doubles';
import { EntryFrame } from './entry-frame';

@Component({
  imports: [EntryFrame],
  template: `
    <app-entry-frame [languageSwitch]="switchOn()">
      <h1>Sign in</h1>
    </app-entry-frame>
  `,
})
class Host {
  readonly switchOn = signal(true);
}

/**
 * The frame of the pages before the shell (Calm Glass, Phase 39.8a): the
 * lockup, the language switch, the projected page on its panel, and the brand
 * side with its one signature moment.
 */
describe('EntryFrame', () => {
  let reload: ReturnType<typeof vi.fn>;
  let storage: Storage;

  function setup(language: 'en' | 'ar' = 'en') {
    reload = vi.fn();
    storage = memoryStorage();
    TestBed.configureTestingModule({
      providers: [
        { provide: PAGE_RELOAD, useValue: reload },
        { provide: LOCAL_STORAGE, useValue: storage },
        { provide: APP_LANGUAGE, useValue: language },
      ],
    });
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();

    return { fixture, root: fixture.nativeElement as HTMLElement };
  }

  it('puts the page inside one main landmark, after the lockup', () => {
    const { root } = setup();
    const main = root.querySelector('main');
    const lockup = root.querySelector('header [data-testid="entry-lockup"]');

    expect(root.querySelectorAll('main')).toHaveLength(1);
    expect(main?.id).toBe('main-content');
    expect(main?.querySelector('h1')?.textContent).toBe('Sign in');
    // The lockup: one image named by the public name, the mark and both scripts.
    expect(lockup?.getAttribute('role')).toBe('img');
    expect(lockup?.getAttribute('aria-label')).toBe('Nabra');
    expect(lockup?.querySelector('app-brand-mark')).not.toBeNull();
    expect(lockup?.querySelectorAll('svg.lockup-name')).toHaveLength(2);
  });

  it('puts the Arabic name first in the lockup on an Arabic page', () => {
    const { root } = setup('ar');
    const names = root.querySelectorAll('[data-testid="entry-lockup"] svg.lockup-name');

    expect(names[0].classList).toContain('lockup-name-arabic');
    expect(names[1].classList).not.toContain('lockup-name-arabic');
  });

  it('offers each language in its own name, language and direction, the current one pressed', () => {
    const { root } = setup('en');
    const english = root.querySelector('[data-testid="entry-language-en"]');
    const arabic = root.querySelector('[data-testid="entry-language-ar"]');

    expect(root.querySelector('[data-testid="entry-language"]')?.getAttribute('aria-label')).toBe(
      'Language',
    );
    expect(english?.textContent?.trim()).toBe('English');
    expect(english?.getAttribute('aria-pressed')).toBe('true');
    expect(arabic?.textContent?.trim()).toBe('العربية');
    expect(arabic?.getAttribute('lang')).toBe('ar');
    expect(arabic?.getAttribute('dir')).toBe('rtl');
    expect(arabic?.getAttribute('aria-pressed')).toBe('false');
  });

  it('stores the choice and reloads into the other language', () => {
    const { root } = setup('en');

    (root.querySelector('[data-testid="entry-language-ar"]') as HTMLButtonElement).click();

    expect(storage.getItem(LANGUAGE_STORAGE_KEY)).toBe('ar');
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not reload when the current language is chosen again', () => {
    const { root } = setup('ar');

    (root.querySelector('[data-testid="entry-language-ar"]') as HTMLButtonElement).click();

    expect(reload).not.toHaveBeenCalled();
  });

  it('withholds the switch when the page says a reload would lose something', () => {
    const { fixture, root } = setup();

    fixture.componentInstance.switchOn.set(false);
    fixture.detectChanges();

    expect(root.querySelector('[data-testid="entry-language"]')).toBeNull();
  });

  it('lays the brand side out after the form, hidden from assistive technology', () => {
    const { root } = setup();
    const brand = root.querySelector('aside[data-testid="entry-brand"]');
    const h1 = root.querySelector('main h1');

    // Decoration: nothing on it is a reader's to find, so it is out of the tree.
    expect(brand?.getAttribute('aria-hidden')).toBe('true');
    expect(brand?.querySelectorAll('a, button, input, h1, h2, h3')).toHaveLength(0);
    // The form first in the document, so a phone shows it first and a keyboard
    // and a screen reader reach it first.
    expect(h1!.compareDocumentPosition(brand!) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    // The one serif line and the sentence of promise.
    expect(brand?.querySelector('.entry-tagline')?.textContent?.trim()).toBe(
      'Two voices. One tone.',
    );
    expect(brand?.querySelector('.entry-promise')?.textContent).toContain(
      'A person on your team steps in',
    );
    // The Phase 36 example conversation is gone: one line, one sentence.
    expect(brand?.querySelector('figure, .bubble')).toBeNull();
  });

  it('draws the mark as inline SVG, never as an image to download', () => {
    const { root } = setup();
    const brand = root.querySelector('.entry-brand')!;

    expect(brand.querySelector('svg.entry-brand-mark circle.mark-ai')).not.toBeNull();
    expect(brand.querySelector('svg.entry-brand-mark circle.mark-team')).not.toBeNull();
    expect(brand.querySelector('svg.entry-brand-mark path.mark-lens')).not.toBeNull();
    expect(root.querySelector('img, picture, video, canvas')).toBeNull();
  });

  it('plays the signature once per page load, never on the second frame', () => {
    const { root } = setup();
    const second = TestBed.createComponent(Host);
    second.detectChanges();

    expect(root.querySelector('.entry-brand-mark')?.classList).toContain('is-settling');
    expect(
      (second.nativeElement as HTMLElement).querySelector('.entry-brand-mark')?.classList,
    ).not.toContain('is-settling');
  });

  it('puts the form on one panel inside the main landmark', () => {
    const { root } = setup('ar');
    const panel = root.querySelector('main .entry-panel');

    expect(panel?.querySelector('h1')?.textContent).toBe('Sign in');
  });
});
