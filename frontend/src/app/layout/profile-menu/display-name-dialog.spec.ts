import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AuthService } from '@app/core/auth/auth-service';
import { ToastService } from '@app/core/feedback/toast-service';
import { LanguageService } from '@app/core/i18n/language-service';
import { stubDialogs } from '@app/testing/test-doubles';
import { DisplayNameDialog } from './display-name-dialog';
import {
  DISPLAY_NAME_LIMIT,
  hasControlCharacters,
  normaliseDisplayName,
} from './display-name-problems';

/**
 * The member's own display name (Phase 39.6a).
 *
 * What matters: it opens on the saved name; the two rules are said before a
 * round trip, in the server's words; a refusal from the server is worded by its
 * code and keeps what was typed; a save shows at once; clearing sends null; and
 * nothing is sent twice.
 */
describe('DisplayNameDialog', () => {
  let fixture: ComponentFixture<DisplayNameDialog>;
  let saved: (string | null)[];
  let refuseWith: HttpErrorResponse | null;
  let toasts: string[];
  let pending: { resolve: () => void } | null;

  async function open(current: string | null = 'Sara Ahmed', hold = false): Promise<void> {
    saved = [];
    toasts = [];
    refuseWith = null;
    pending = null;

    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuthService,
          useValue: {
            saveDisplayName: (name: string | null) => {
              saved.push(name);

              if (refuseWith) {
                return Promise.reject(refuseWith);
              }

              return hold
                ? new Promise<void>((resolve) => (pending = { resolve }))
                : Promise.resolve();
            },
          },
        },
        { provide: ToastService, useValue: { success: (text: string) => toasts.push(text) } },
        { provide: LanguageService, useValue: { current: 'en' } },
      ],
    });

    fixture = TestBed.createComponent(DisplayNameDialog);
    fixture.componentRef.setInput('current', current);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    stubDialogs(fixture.nativeElement);
    fixture.componentRef.setInput('requests', 1);
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    await new Promise((resolve) => setTimeout(resolve, 0));
    fixture.detectChanges();
  }

  afterEach(() => (fixture?.nativeElement as HTMLElement | undefined)?.remove());

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const dialog = (): HTMLDialogElement => host().querySelector('dialog')!;
  const field = (): HTMLInputElement =>
    host().querySelector<HTMLInputElement>('[data-testid="display-name-input"]')!;
  const problem = (): string =>
    host().querySelector('[data-testid="display-name-problem"]')?.textContent?.trim() ?? '';

  async function type(value: string): Promise<void> {
    field().value = value;
    field().dispatchEvent(new Event('input'));
    await settle();
  }

  async function submit(): Promise<void> {
    host().querySelector<HTMLButtonElement>('[data-testid="save-display-name"]')!.click();
    await settle();
  }

  it('opens on the saved name, named by its heading and described by its sentence', async () => {
    await open();

    expect(dialog().hasAttribute('open')).toBe(true);
    expect(field().value).toBe('Sara Ahmed');
    expect(field().getAttribute('dir')).toBe('auto');
    expect(field().getAttribute('autocomplete')).toBe('name');
    expect(dialog().getAttribute('aria-labelledby')).toBe('display-name-title');
    expect(host().querySelector('#display-name-title')?.textContent?.trim()).toBe(
      'Your display name',
    );
    expect(host().querySelector('#display-name-lede')?.textContent).toContain(
      'Customers never see it',
    );
    expect(host().querySelector('label[for="display-name-input"]')?.textContent?.trim()).toBe(
      'Display name',
    );
  });

  it('saves the name, says so, and closes', async () => {
    await open(null);
    await type('  Omar   Saleh ');
    await submit();

    expect(saved).toEqual(['Omar Saleh']);
    expect(toasts).toEqual(['Your display name is saved.']);
    expect(dialog().hasAttribute('open')).toBe(false);
  });

  it('clears the name by sending null, and says the address shows instead', async () => {
    await open('Sara Ahmed');
    await type('   ');
    await submit();

    expect(saved).toEqual([null]);
    expect(toasts[0]).toContain('Your team sees your email address');
  });

  it('closes without a request when nothing changed', async () => {
    await open('Sara Ahmed');
    await type(' Sara  Ahmed ');
    await submit();

    expect(saved).toEqual([]);
    expect(dialog().hasAttribute('open')).toBe(false);
  });

  it('says the 80-character rule before sending, and keeps the name typed', async () => {
    await open(null);

    await type('a'.repeat(DISPLAY_NAME_LIMIT));
    expect(problem()).toBe('');

    await type('a'.repeat(DISPLAY_NAME_LIMIT + 1));
    await submit();

    expect(saved).toEqual([]);
    expect(problem()).toBe(
      'Your display name can be at most 80 characters. Shorten it and save again.',
    );
    expect(field().getAttribute('aria-invalid')).toBe('true');
    expect(field().getAttribute('aria-describedby')).toContain('display-name-problem');
    expect(field().value.length).toBe(81);
    expect(dialog().hasAttribute('open')).toBe(true);
  });

  it('refuses a direction mark before sending, and keeps the two joiners', async () => {
    await open(null);
    await type('Sara‎Ahmed');
    await submit();

    expect(saved).toEqual([]);
    expect(problem()).toContain('hidden character');

    await type('ل‌ا');
    await submit();

    expect(saved).toEqual(['ل‌ا']);
  });

  it('words a refusal from the server by its code, never by its English detail', async () => {
    await open(null);
    refuseWith = new HttpErrorResponse({
      status: 400,
      error: {
        title: 'Bad',
        detail: 'The English detail.',
        code: 'account.display_name.too_long',
        limit: 80,
        limitUnit: 'characters',
      },
    });
    await type('Omar');
    await submit();

    expect(problem()).toBe(
      'Your display name can be at most 80 characters. Shorten it and save again.',
    );
    expect(host().textContent).not.toContain('The English detail.');
    expect(field().value).toBe('Omar');
    expect(dialog().hasAttribute('open')).toBe(true);
  });

  it('keeps the generic words for a code it does not know', async () => {
    await open(null);
    refuseWith = new HttpErrorResponse({
      status: 400,
      error: { title: 'Bad', detail: 'The English detail.', code: 'account.something.new' },
    });
    await type('Omar');
    await submit();

    expect(problem()).not.toBe('');
    expect(problem()).not.toContain('The English detail.');
  });

  it('sends one request however often Save is pressed while it runs', async () => {
    await open(null, true);
    await type('Omar');
    await submit();
    await submit();

    expect(saved).toEqual(['Omar']);
    expect(
      host().querySelector('[data-testid="save-display-name"]')?.getAttribute('aria-disabled'),
    ).toBe('true');

    pending!.resolve();
    await settle();

    expect(dialog().hasAttribute('open')).toBe(false);
  });

  it('closes on Cancel without saving, and opens again on the saved name', async () => {
    await open('Sara Ahmed');
    await type('Somebody else');

    host().querySelector<HTMLButtonElement>('.confirm-actions .btn-secondary')!.click();
    await settle();

    expect(saved).toEqual([]);
    expect(dialog().hasAttribute('open')).toBe(false);

    fixture.componentRef.setInput('requests', 2);
    await settle();

    expect(field().value).toBe('Sara Ahmed');
  });
});

describe('display name rules', () => {
  it('trims, makes inner spaces one, and reads empty as no name', () => {
    expect(normaliseDisplayName('  Omar \t  Saleh ')).toBe('Omar Saleh');
    expect(normaliseDisplayName('   ')).toBeNull();
    expect(normaliseDisplayName('')).toBeNull();
  });

  it('finds control and format characters, but not the two joiners', () => {
    expect(hasControlCharacters('Sara\u0007')).toBe(true);
    expect(hasControlCharacters('Sara‮Haddad')).toBe(true);
    expect(hasControlCharacters('ل‌ا')).toBe(false);
    expect(hasControlCharacters('\u{1F469}‍\u{1F4BB} Sara')).toBe(false);
    expect(hasControlCharacters('سارة أحمد')).toBe(false);
  });
});
