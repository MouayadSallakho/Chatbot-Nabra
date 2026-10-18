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
 * The frame of the pages before the shell: the lockup, the language switch,
 * the projected page, and the story panel with its one signature moment.
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

    expect(root.querySelectorAll('main')).toHaveLength(1);
    expect(main?.id).toBe('main-content');
    expect(main?.querySelector('h1')?.textContent).toBe('Sign in');
    expect(root.querySelector('header .entry-wordmark')?.textContent?.trim()).toBe('Nabra');
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

  it('names the story panel by its tagline and hides the example from assistive technology', () => {
    const { root } = setup();
    const story = root.querySelector('aside[data-testid="entry-story"]');
    const example = story?.querySelector('figure');

    expect(story?.getAttribute('aria-labelledby')).toBe('entry-story-title');
    expect(root.querySelector('#entry-story-title')?.textContent?.trim()).toBe(
      'Two voices. One tone.',
    );
    expect(example?.getAttribute('aria-hidden')).toBe('true');
    // Labelled as an example on screen, so nobody takes it for their own data.
    expect(example?.querySelector('figcaption')?.textContent?.trim()).toBe('Example conversation');
    // The handoff is drawn with the product's own pieces: an AI reply, then a person.
    expect(example?.querySelector('.bubble-ai')).not.toBeNull();
    expect(example?.querySelector('.handoff-divider.handoff-to-human')).not.toBeNull();
    expect(example?.querySelector('.bubble-human')).not.toBeNull();
  });

  it('plays the signature once per page load, never on the second frame', () => {
    const { root } = setup();
    const second = TestBed.createComponent(Host);
    second.detectChanges();

    expect(root.querySelector('.entry-story-mark')?.classList).toContain('is-settling');
    expect(
      (second.nativeElement as HTMLElement).querySelector('.entry-story-mark')?.classList,
    ).not.toContain('is-settling');
  });

  it('keeps the story mark out of the accessibility tree', () => {
    const { root } = setup('ar');

    expect(root.querySelector('.entry-story-mark')?.getAttribute('aria-hidden')).toBe('true');
  });
});
