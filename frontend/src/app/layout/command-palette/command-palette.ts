import {
  ChangeDetectionStrategy,
  Component,
  DOCUMENT,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
  viewChild,
} from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { PermissionService } from '@app/core/authorization/permission-service';
import { ThemeService } from '@app/core/theme/theme-service';
import { THEME_LABELS, THEME_PREFERENCES, ThemePreference } from '@app/core/theme/theme';
import { LanguageService } from '@app/core/i18n/language-service';
import { APP_LANGUAGES, LANGUAGE_ENDONYMS, directionOf } from '@app/core/i18n/language';
import { pluralMessage } from '@app/core/i18n/plural';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pathOfUrl } from '@app/core/navigation/route-transition';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { WORKSPACE_SETUP_PATH } from '@app/core/workspace/workspace-guard';
import { NAV_GROUP_LABELS, visibleNavItems } from '@app/layout/navigation';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import {
  COMMAND_GROUPS,
  Command,
  CommandGroup,
  LabelParts,
  labelParts,
  rankCommands,
} from './commands';

const GROUP_LABELS: Readonly<Record<CommandGroup, string>> = {
  navigate: $localize`:@@palette.group.navigate:Go to`,
  actions: $localize`:@@palette.group.actions:Actions`,
  preferences: $localize`:@@palette.group.preferences:Preferences`,
};

const THEME_ICONS: Readonly<Record<ThemePreference, IconName>> = {
  system: 'monitor',
  light: 'sun',
  dark: 'moon',
};

interface Entry {
  readonly command: Command;
  /** Position in the flattened list, which is what the arrow keys walk. */
  readonly index: number;
  readonly parts: LabelParts;
}

interface Section {
  readonly group: CommandGroup;
  readonly label: string;
  readonly entries: readonly Entry[];
}

/**
 * The command palette (Ctrl+K, or Command+K): jump to any page the member may
 * open, or run one of a few quick actions, from the keyboard.
 *
 * <strong>Commands, not data.</strong> It is built on the device from the
 * navigation model and the member's permissions, and it never asks the server
 * anything; it says so under the field, so nobody types a customer's name into
 * it and concludes the customer does not exist.
 *
 * <strong>The pattern.</strong> A modal `<dialog>` (the page behind is inert,
 * Escape and the backdrop dismiss it) holding an editable combobox and a
 * grouped listbox, after the WAI-ARIA combobox pattern: focus stays in the field
 * and `aria-activedescendant` names the highlighted option. The keys follow the
 * product's `Menu`: arrows move and wrap, Enter chooses, Escape closes and
 * returns focus to where it was. A choice that opens a page leaves focus to
 * the page (`RouteFocus` puts it on the new heading).
 *
 * Loaded on first use (`@defer` in the shell), so it costs nothing at startup.
 */
@Component({
  selector: 'app-command-palette',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './command-palette.html',
  styleUrl: './command-palette.scss',
})
export class CommandPalette {
  /** Whether the palette is showing. Owned by the shell. */
  readonly open = input(false);

  /** The palette closed, by a choice, Escape, the close button or the backdrop. */
  readonly closed = output<void>();

  /** The member chose to open the notification panel. */
  readonly notificationsRequested = output<void>();

  private readonly router = inject(Router);
  private readonly permissions = inject(PermissionService);
  private readonly theme = inject(ThemeService);
  private readonly language = inject(LanguageService);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly document = inject(DOCUMENT);
  private readonly injector = inject(Injector);

  private readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialog');
  private readonly field = viewChild.required<ElementRef<HTMLInputElement>>('field');
  private readonly list = viewChild<ElementRef<HTMLElement>>('list');

  protected readonly query = signal('');
  protected readonly active = signal(0);

  /** Where focus was before the palette opened, to return it on dismissal. */
  private returnFocusTo: HTMLElement | null = null;

  /** The page being shown, for the "Current page" hint. */
  private readonly currentPath = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => pathOfUrl(this.router, this.router.url)),
    ),
    { initialValue: pathOfUrl(this.router, this.router.url) },
  );

  private readonly commands = computed<readonly Command[]>(() => [
    ...this.navigationCommands(),
    ...this.actionCommands(),
    ...this.preferenceCommands(),
  ]);

  protected readonly sections = computed<readonly Section[]>(() => {
    const query = this.query();
    const ranked = rankCommands(this.commands(), query);
    let index = 0;

    return COMMAND_GROUPS.map((group) => ({
      group,
      label: GROUP_LABELS[group],
      entries: ranked
        .filter((command) => command.group === group)
        .map((command) => ({ command, index: 0, parts: labelParts(command.label, query) })),
    }))
      .filter((section) => section.entries.length > 0)
      .map((section) => ({
        ...section,
        entries: section.entries.map((entry) => ({ ...entry, index: index++ })),
      }));
  });

  private readonly entries = computed(() => this.sections().flatMap((section) => section.entries));

  protected readonly count = computed(() => this.entries().length);

  protected readonly activeId = computed(() => {
    const entry = this.entries()[this.active()];

    return entry ? this.optionId(entry.command) : null;
  });

  /** What a screen reader hears as the list narrows. */
  protected readonly resultsLabel = computed(() => {
    const count = this.count();

    if (count === 0) {
      return $localize`:@@palette.results.none:No results`;
    }

    const n = formatNumber(this.language.current, count);

    return pluralMessage(this.language.current, count, {
      zero: $localize`:@@palette.results.zero:No results`,
      one: $localize`:@@palette.results.one:${n}:count: result`,
      two: $localize`:@@palette.results.two:${n}:count: results`,
      few: $localize`:@@palette.results.few:${n}:count: results`,
      many: $localize`:@@palette.results.many:${n}:count: results`,
      other: $localize`:@@palette.results.other:${n}:count: results`,
    });
  });

  protected readonly noResults = computed(
    () =>
      $localize`:@@palette.noResults:No page or action matches “${this.query().trim()}:query:”.`,
  );

  constructor() {
    effect(() => {
      const open = this.open();

      untracked(() => (open ? this.show() : this.hide()));
    });
  }

  protected optionId(command: Command): string {
    return `palette-option-${command.id}`;
  }

  protected onInput(event: Event): void {
    this.query.set((event.target as HTMLInputElement).value);
    this.active.set(0);
    this.scrollActiveIntoView();
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.count();

    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (count > 0) {
          this.active.set((this.active() + 1) % count);
          this.scrollActiveIntoView();
        }
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (count > 0) {
          this.active.set((this.active() - 1 + count) % count);
          this.scrollActiveIntoView();
        }
        break;
      case 'Enter': {
        const entry = this.entries()[this.active()];

        event.preventDefault();
        if (entry) {
          this.choose(entry.command);
        }
        break;
      }
      case 'Escape':
        // One Escape, one layer: the shell's drawer and the bell also listen at
        // the document, and must not close as well.
        event.preventDefault();
        event.stopPropagation();
        this.dismiss();
        break;
    }
  }

  /** The browser's own Escape handling of a modal dialog, routed through ours. */
  protected onCancel(event: Event): void {
    event.preventDefault();
    this.dismiss();
  }

  /** A press on the backdrop lands on the dialog element itself. */
  protected onDialogClick(event: MouseEvent): void {
    if (event.target === this.dialog().nativeElement) {
      this.dismiss();
    }
  }

  protected point(index: number): void {
    if (this.active() !== index) {
      this.active.set(index);
    }
  }

  protected choose(command: Command): void {
    const opensPage = command.group === 'navigate' || command.id.startsWith('action-go-');

    this.close(!opensPage);
    command.run();
  }

  protected dismiss(): void {
    this.close(true);
  }

  private close(restoreFocus: boolean): void {
    const dialog = this.dialog().nativeElement;

    if (dialog.open) {
      dialog.close();
    }

    if (restoreFocus && this.returnFocusTo?.isConnected) {
      this.returnFocusTo.focus();
    }

    this.returnFocusTo = null;
    this.closed.emit();
  }

  private show(): void {
    afterNextRender(
      () => {
        const dialog = this.dialog().nativeElement;

        if (dialog.open) {
          return;
        }

        const focused = this.document.activeElement;

        this.returnFocusTo = focused instanceof HTMLElement ? focused : null;
        this.query.set('');
        this.active.set(0);
        this.field().nativeElement.value = '';
        dialog.showModal();
        this.field().nativeElement.focus();
      },
      { injector: this.injector },
    );
  }

  private hide(): void {
    const dialog = this.dialog().nativeElement;

    if (dialog.open) {
      this.close(true);
    }
  }

  private scrollActiveIntoView(): void {
    afterNextRender(
      () => {
        this.list()
          ?.nativeElement.querySelector('[aria-selected="true"]')
          ?.scrollIntoView?.({ block: 'nearest' });
      },
      { injector: this.injector },
    );
  }

  // ---------------------------------------------------------------------------
  // The commands
  // ---------------------------------------------------------------------------

  private navigationCommands(): Command[] {
    const here = this.currentPath();
    const current = $localize`:@@palette.currentPage:Current page`;
    const items = visibleNavItems((...codes) => this.permissions.hasAny(...codes)).map(
      (item): Command => ({
        id: `go-${item.path}`,
        group: 'navigate',
        label: item.label,
        keywords: [NAV_GROUP_LABELS[item.group]],
        icon: item.icon,
        hint: here === `/${item.path}` ? current : undefined,
        run: () => void this.router.navigateByUrl(`/${item.path}`),
      }),
    );

    // Every member has notifications; nothing to gate them on.
    const own: Command[] = [
      {
        id: 'go-notifications',
        group: 'navigate',
        label: $localize`:@@palette.go.notifications:Notifications`,
        keywords: [$localize`:@@palette.keywords.notifications:alerts history`],
        icon: 'bell',
        hint: here === '/notifications' ? current : undefined,
        run: () => void this.router.navigateByUrl('/notifications'),
      },
      {
        id: 'go-notification-preferences',
        group: 'navigate',
        label: $localize`:@@palette.go.notificationPreferences:Notification preferences`,
        keywords: [$localize`:@@palette.keywords.notificationPreferences:email settings`],
        icon: 'settings',
        hint: here === '/notifications/preferences' ? current : undefined,
        run: () => void this.router.navigateByUrl('/notifications/preferences'),
      },
    ];

    return [...items, ...own];
  }

  /**
   * Quick actions, each offered only to a member who may do it -- the same
   * permission the page's own button is behind. Hiding is not authorisation:
   * the server refuses regardless.
   */
  private actionCommands(): Command[] {
    const actions: Command[] = [];

    if (this.permissions.has('ai-agents.create')) {
      actions.push({
        id: 'action-go-new-agent',
        group: 'actions',
        label: $localize`:@@palette.action.newAgent:Create an AI agent`,
        keywords: [$localize`:@@palette.keywords.newAgent:new add assistant`],
        icon: 'plus',
        run: () => void this.router.navigateByUrl('/ai-agents/new'),
      });
    }

    if (this.permissions.has('knowledge.manage')) {
      actions.push({
        id: 'action-go-upload-knowledge',
        group: 'actions',
        label: $localize`:@@palette.action.uploadKnowledge:Upload a knowledge file`,
        keywords: [$localize`:@@palette.keywords.uploadKnowledge:add document file import`],
        icon: 'knowledge',
        run: () => void this.router.navigateByUrl('/knowledge/upload'),
      });
    }

    if (this.permissions.has('members.invite')) {
      actions.push({
        id: 'action-go-invite-member',
        group: 'actions',
        label: $localize`:@@palette.action.inviteMember:Invite a member`,
        keywords: [$localize`:@@palette.keywords.inviteMember:add teammate people team`],
        icon: 'team',
        run: () => void this.router.navigateByUrl('/team/invite'),
      });
    }

    actions.push({
      id: 'action-open-notifications',
      group: 'actions',
      label: $localize`:@@palette.action.openNotifications:Open the notification panel`,
      keywords: [$localize`:@@palette.keywords.openNotifications:alerts bell unread`],
      icon: 'bell',
      run: () => this.notificationsRequested.emit(),
    });

    if (this.workspace.hasChoice()) {
      actions.push({
        id: 'action-go-switch-workspace',
        group: 'actions',
        label: $localize`:@@palette.action.switchWorkspace:Switch workspace`,
        keywords: [$localize`:@@palette.keywords.switchWorkspace:change organisation`],
        icon: 'switch',
        run: () => void this.router.navigate(['/', WORKSPACE_SETUP_PATH]),
      });
    }

    return actions;
  }

  private preferenceCommands(): Command[] {
    const chosen = this.theme.preference();
    const inUse = $localize`:@@palette.inUse:In use`;
    const themeWords = $localize`:@@palette.keywords.theme:theme appearance colour mode`;
    const themes = THEME_PREFERENCES.map((value): Command => ({
      id: `theme-${value}`,
      group: 'preferences',
      label: $localize`:@@palette.pref.theme:Theme: ${THEME_LABELS[value]}:name:`,
      keywords: [themeWords],
      icon: THEME_ICONS[value],
      hint: chosen === value ? inUse : undefined,
      run: () => this.theme.select(value),
    }));

    // Each language is offered in its own name, as in the account menu; the
    // one in use is listed too, so nobody wonders where it went.
    const languageWord = $localize`:@@palette.pref.language:Language`;
    const languages = APP_LANGUAGES.map((value): Command => ({
      id: `language-${value}`,
      group: 'preferences',
      label: LANGUAGE_ENDONYMS[value],
      keywords: [languageWord, 'language', 'لغة'],
      icon: 'switch',
      hint: this.language.current === value ? inUse : languageWord,
      lang: value,
      dir: directionOf(value),
      run: () => this.language.choose(value),
    }));

    return [...themes, ...languages];
  }
}
