import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { PermissionService } from '@app/core/authorization/permission-service';
import { PermissionCode } from '@app/core/authorization/permission-codes';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { PAGE_RELOAD } from '@app/core/i18n/language-service';
import { ThemeService } from '@app/core/theme/theme-service';
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { CommandPalette } from './command-palette';

@Component({ template: '' })
class Blank {}

/**
 * The palette, through what a reader meets: a modal dialog, a combobox that
 * keeps focus, a grouped listbox, and the keys of the product's Menu.
 *
 * jsdom implements no modal dialogs, so `showModal` and `close` are stood in
 * for at the prototype -- the palette's dialog only exists once it renders.
 */
describe('CommandPalette', () => {
  let fixture: ComponentFixture<CommandPalette>;
  let router: Router;
  let reload: ReturnType<typeof vi.fn>;
  const original = {
    showModal: HTMLDialogElement.prototype.showModal,
    close: HTMLDialogElement.prototype.close,
  };

  beforeEach(() => {
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
      this.dispatchEvent(new Event('close'));
    };
  });

  afterEach(() => {
    HTMLDialogElement.prototype.showModal = original.showModal;
    HTMLDialogElement.prototype.close = original.close;
    root()?.remove();
    document.getElementById('opener')?.remove();
  });

  const root = (): HTMLElement => fixture?.nativeElement as HTMLElement;
  const dialog = (): HTMLDialogElement => root().querySelector('dialog')!;
  const field = (): HTMLInputElement => root().querySelector('[role="combobox"]')!;
  const options = (): HTMLElement[] => [...root().querySelectorAll<HTMLElement>('[role="option"]')];
  const labels = (): string[] =>
    options().map((option) => option.querySelector('.palette-option-label')!.textContent!.trim());
  const selected = (): HTMLElement | undefined =>
    options().find((option) => option.getAttribute('aria-selected') === 'true');

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function render(
    granted: readonly PermissionCode[] = [],
    options: { hasChoice?: boolean } = {},
  ): Promise<void> {
    reload = vi.fn();

    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '**', component: Blank }]),
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        { provide: PAGE_RELOAD, useValue: reload },
        {
          provide: MEDIA_QUERY_FACTORY,
          useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
        },
        {
          provide: PermissionService,
          useValue: {
            has: (code: PermissionCode) => granted.includes(code),
            hasAny: (...codes: PermissionCode[]) => codes.some((code) => granted.includes(code)),
          },
        },
        {
          provide: WorkspaceContextService,
          useValue: { hasChoice: signal(options.hasChoice ?? false) },
        },
      ],
    });

    router = TestBed.inject(Router);
    fixture = TestBed.createComponent(CommandPalette);
    document.body.appendChild(root());
    await settle();
  }

  /** As the shell does: `open` goes back to false on `closed`, then true again. */
  async function open(): Promise<void> {
    fixture.componentRef.setInput('open', false);
    await settle();
    fixture.componentRef.setInput('open', true);
    await settle();
  }

  async function type(text: string): Promise<void> {
    field().value = text;
    field().dispatchEvent(new Event('input'));
    await settle();
  }

  async function press(key: string): Promise<void> {
    field().dispatchEvent(new KeyboardEvent('keydown', { key, bubbles: true, cancelable: true }));
    await settle();
  }

  it('opens as a modal dialog with focus in its field', async () => {
    await render();
    await open();

    expect(dialog().hasAttribute('open')).toBe(true);
    expect(dialog().getAttribute('aria-labelledby')).toBe('palette-title');
    expect(document.activeElement).toBe(field());
  });

  it('is an editable combobox that names the highlighted option, after the APG pattern', async () => {
    await render();
    await open();

    const listbox = root().querySelector('[role="listbox"]')!;

    expect(field().getAttribute('aria-controls')).toBe(listbox.id);
    expect(field().getAttribute('aria-autocomplete')).toBe('list');
    expect(field().getAttribute('aria-expanded')).toBe('true');
    expect(field().getAttribute('aria-activedescendant')).toBe(selected()?.id);
    expect(options()[0]).toBe(selected());

    // Sections are groups labelled by their heading.
    const groups = [...listbox.querySelectorAll('[role="group"]')].map((group) =>
      document.getElementById(group.getAttribute('aria-labelledby')!)?.textContent?.trim(),
    );

    expect(groups).toEqual(['Go to', 'Actions', 'Preferences']);
  });

  it('says it finds pages and actions, and not records', async () => {
    await render();
    await open();

    const scope = document.getElementById(field().getAttribute('aria-describedby')!);

    expect(scope?.textContent).toContain('It does not search conversations, customers');
  });

  it('offers only the destinations and actions the member may use', async () => {
    await render(['conversations.view', 'knowledge.view']);
    await open();

    const shown = labels();

    expect(shown).toContain('Overview');
    expect(shown).toContain('Conversations');
    expect(shown).toContain('Knowledge');
    expect(shown).toContain('Notifications');
    expect(shown).not.toContain('Analytics');
    expect(shown).not.toContain('Billing');
    expect(shown).not.toContain('Create an AI agent');
    expect(shown).not.toContain('Upload a knowledge file');
    expect(shown).not.toContain('Invite a member');
    expect(shown).not.toContain('Switch workspace');
  });

  it('offers each quick action behind the permission its page uses', async () => {
    await render(['ai-agents.create', 'knowledge.manage', 'members.invite'], { hasChoice: true });
    await open();

    expect(labels()).toEqual(
      expect.arrayContaining([
        'Create an AI agent',
        'Upload a knowledge file',
        'Invite a member',
        'Open the notification panel',
        'Switch workspace',
        'Theme: System',
        'Theme: Light',
        'Theme: Dark',
        'English',
        'العربية',
      ]),
    );
  });

  it('narrows as the reader types, and says how many remain', async () => {
    await render(['knowledge.view', 'knowledge.manage']);
    await open();
    await type('know');

    expect(labels()).toEqual(['Knowledge', 'Upload a knowledge file']);
    expect(root().querySelector('[data-testid="palette-results"]')?.textContent?.trim()).toBe(
      '2 results',
    );
    // The typed part is emphasised, as text.
    expect(options()[0].querySelector('mark')?.textContent).toBe('Know');
  });

  it('moves with the arrow keys, wrapping at both ends', async () => {
    await render();
    await open();

    const count = options().length;

    await press('ArrowUp');
    expect(selected()).toBe(options()[count - 1]);

    await press('ArrowDown');
    expect(selected()).toBe(options()[0]);

    await press('ArrowDown');
    expect(selected()).toBe(options()[1]);
    expect(field().getAttribute('aria-activedescendant')).toBe(options()[1].id);
  });

  it('opens the highlighted page on Enter, and closes', async () => {
    await render(['analytics.view']);
    const navigate = vi.spyOn(router, 'navigateByUrl');
    let closed = 0;

    fixture.componentInstance.closed.subscribe(() => closed++);
    await open();
    await type('analytics');
    await press('Enter');

    expect(navigate).toHaveBeenCalledWith('/analytics');
    expect(dialog().hasAttribute('open')).toBe(false);
    expect(closed).toBe(1);
  });

  it('closes on Escape and gives focus back to where it was', async () => {
    await render();

    const opener = document.createElement('button');

    opener.id = 'opener';
    document.body.appendChild(opener);
    opener.focus();

    await open();
    await press('Escape');

    expect(dialog().hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(opener);
  });

  it('says plainly when nothing matches', async () => {
    await render();
    await open();
    await type('zzzz');

    expect(options()).toHaveLength(0);
    expect(field().getAttribute('aria-expanded')).toBe('false');
    expect(root().querySelector('[data-testid="palette-empty"]')?.textContent).toContain(
      'No page or action matches “zzzz”.',
    );
  });

  it('switches the theme, and marks the one in use', async () => {
    await render();
    await open();
    await type('dark');
    await press('Enter');

    expect(TestBed.inject(ThemeService).preference()).toBe('dark');

    await open();
    await type('theme');

    const dark = options().find((option) => option.textContent?.includes('Theme: Dark'));

    expect(dark?.textContent).toContain('In use');
  });

  it('offers the other language in its own name and direction, and restarts in it', async () => {
    await render();
    await open();

    const arabic = root().querySelector<HTMLElement>('[data-testid="palette-option-language-ar"]')!;
    const label = arabic.querySelector('.palette-option-label')!;

    expect(label.getAttribute('lang')).toBe('ar');
    expect(label.getAttribute('dir')).toBe('rtl');

    arabic.click();
    await settle();

    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('asks the shell to open the notification panel', async () => {
    await render();
    let requested = 0;

    fixture.componentInstance.notificationsRequested.subscribe(() => requested++);
    await open();
    await type('notification panel');
    await press('Enter');

    expect(requested).toBe(1);
  });

  it('runs a command chosen with the pointer', async () => {
    await render(['customers.view']);
    const navigate = vi.spyOn(router, 'navigateByUrl');

    await open();
    options()
      .find((option) => option.textContent?.includes('Customers'))
      ?.click();
    await settle();

    expect(navigate).toHaveBeenCalledWith('/customers');
  });
});
