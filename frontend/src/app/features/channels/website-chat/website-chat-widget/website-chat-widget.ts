import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom, map } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { APP_LANGUAGE } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import {
  WebChatAdminApi,
  WebChatWidgetConfiguration,
} from '@app/features/channels/website-chat-api';
import {
  BOUNDARY_CONTRAST,
  ChatTheme,
  DEFAULT_ACCENT,
  chatBrand,
  checkAccent,
  checkChatBrand,
  isAccent,
} from '@app/features/embed/accent-color';
import { LauncherPosition, physicalSide } from '@app/features/embed/launcher-position';
import { ChatLauncher } from '@app/shared/chat/chat-launcher';
import { ChatPanel } from '@app/shared/chat/chat-panel';
import { Radio } from '@app/shared/choice/choice';
import { Icon } from '@app/shared/icon/icon';
import { ScrollRegion } from '@app/shared/scroll-region/scroll-region';
import { checkOrigin } from '../allowed-origin';

/** The server's bound on a greeting, so the box can say so before sending. */
const MAX_GREETING_LENGTH = 300;

/** One greeting box on the form. */
interface GreetingBox {
  readonly id: string;
  /** What the server calls it in a refusal's `field`. */
  readonly field: string;
  readonly control: 'greeting' | 'greetingEn' | 'greetingAr';
  readonly testId: string;
  readonly dir: 'ltr' | 'rtl' | 'auto';
  readonly lang: 'en' | 'ar' | null;
  readonly label: string;
  readonly hint: string;
}

/** The box for each greeting the server can name in a refusal's `field`. */
const GREETING_FIELD_IDS: Readonly<Record<string, string>> = {
  greeting: 'widget-greeting',
  'greetings.en': 'widget-greeting-en',
  'greetings.ar': 'widget-greeting-ar',
};

/** Six-digit hex, which is the only colour the server stores. */
const ACCENT_PATTERN = /^#[0-9a-fA-F]{6}$/;

/**
 * Installing the widget on a website, and deciding how it looks there.
 *
 * <strong>A section of the chat link screen, kept as its own component.</strong>
 * It has its own read, its own version and its own conditional write, and mixing
 * four forms and an allowlist into the page that publishes the link would produce
 * a component nobody could hold in their head.
 *
 * <strong>The allowlist is the part that matters, and it is treated as a
 * security control rather than a setting.</strong> Adding a website is what makes
 * the widget run there. Removing one stops it immediately, including for
 * conversations already open, because the runtime re-reads this list on every
 * message rather than trusting the session that was opened earlier.
 *
 * <strong>A refused save keeps what the reader typed.</strong> A `412` means
 * somebody else changed this endpoint since it was read. Nothing is retried
 * automatically and nothing is discarded: replaying an allowlist against state
 * nobody has seen is exactly the lost update the precondition exists to prevent,
 * and here it could put a removed website back.
 *
 * <strong>What visitors will see, while it is chosen</strong> (Phase 36.9). The
 * preview beside the appearance form is the real widget, the same launcher and
 * panel components the iframe runs, drawn from the values on the form before
 * they are saved. The accent is checked by the widget's own rules
 * (`accent-color.ts`), and the result is said in words: the label's contrast,
 * and whether the launcher stands out on a white and on a dark page. Nothing
 * here refuses a colour, because the rules make any colour legible; the screen
 * says what they did.
 */
@Component({
  selector: 'app-website-chat-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, ScrollRegion, ChatPanel, ChatLauncher, Icon, Radio],
  templateUrl: './website-chat-widget.html',
  styleUrl: './website-chat-widget.scss',
})
export class WebsiteChatWidget implements OnInit {
  private readonly api = inject(WebChatAdminApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly language = inject(APP_LANGUAGE);

  /** The endpoint being configured. */
  readonly webChatEndpointId = input.required<string>();

  /** The agent's name: the widget's title in the preview. */
  readonly agentName = input('');

  /** Raised after a successful save, so the page's facts can be re-read. */
  readonly saved = output<void>();

  protected readonly loading = signal(true);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly configuration = signal<WebChatWidgetConfiguration | null>(null);
  protected readonly saving = signal(false);
  protected readonly saveFailure = signal<ApiError | null>(null);
  protected readonly stale = signal(false);
  protected readonly savedAt = signal(false);
  protected readonly copied = signal<'snippet' | 'loader' | null>(null);

  /** The websites being edited, before anything is sent. */
  protected readonly origins = signal<readonly string[]>([]);
  protected readonly originProblem = signal<string | null>(null);

  /** The version the next conditional write must present. */
  private readonly version = signal<string | null>(null);

  protected readonly canManage = computed(() => this.permissions.has('website-chat.manage'));

  protected readonly maxGreetingLength = MAX_GREETING_LENGTH;

  protected readonly maxOrigins = computed(() => this.configuration()?.maxAllowedOrigins ?? 0);

  protected readonly originsFull = computed(() => this.origins().length >= this.maxOrigins());

  /**
   * Whether the widget is installed anywhere.
   *
   * An endpoint with no approved website is not broken — it is simply not
   * embedded yet — but somebody who pasted the snippet and saw nothing needs to
   * be told which half is missing.
   */
  protected readonly noOrigins = computed(() => this.origins().length === 0);

  protected readonly form = new FormGroup({
    greeting: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_GREETING_LENGTH)],
    }),
    greetingEn: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_GREETING_LENGTH)],
    }),
    greetingAr: new FormControl('', {
      nonNullable: true,
      validators: [Validators.maxLength(MAX_GREETING_LENGTH)],
    }),
    accentColor: new FormControl(DEFAULT_ACCENT, {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(ACCENT_PATTERN)],
    }),
    launcherPosition: new FormControl<LauncherPosition>('Right', { nonNullable: true }),
    theme: new FormControl<'System' | 'Light' | 'Dark'>('System', { nonNullable: true }),
  });

  /** The form as signals, for the preview: it changes as the reader types. */
  private readonly values = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    {
      initialValue: this.form.getRawValue(),
    },
  );

  /** The accent as the widget would apply it: checked, with its label colour. */
  protected readonly accent = computed(() => checkAccent(this.values().accentColor));

  /** The colour inside the chat, per theme, as visitors would see it (emerald where it fails). */
  protected readonly brand = computed(() => chatBrand(this.accent().accent));

  /** Whether the colour box holds something the server will accept. */
  protected readonly accentInvalid = computed(() => !isAccent(this.values().accentColor));

  /**
   * The greeting a visitor reading this page's language would see: theirs when
   * it is written, else the default (Phase 39.1).
   */
  protected readonly previewGreeting = computed(() => {
    const values = this.values();
    const own = (this.language === 'ar' ? values.greetingAr : values.greetingEn).trim();
    const fallback = values.greeting.trim();

    return own || fallback || null;
  });

  /**
   * The three greeting boxes: English and Arabic, each in its own direction,
   * then the default, for visitors whose language has none of its own.
   */
  protected readonly greetingBoxes: readonly GreetingBox[] = [
    {
      id: 'widget-greeting-en',
      field: 'greetings.en',
      control: 'greetingEn',
      testId: 'greeting-en',
      dir: 'ltr',
      lang: 'en',
      label: $localize`:@@widget.greeting.en:Greeting in English`,
      hint: $localize`:@@widget.greeting.en.hint:For visitors reading the chat in English.`,
    },
    {
      id: 'widget-greeting-ar',
      field: 'greetings.ar',
      control: 'greetingAr',
      testId: 'greeting-ar',
      dir: 'rtl',
      lang: 'ar',
      label: $localize`:@@widget.greeting.ar:Greeting in Arabic`,
      hint: $localize`:@@widget.greeting.ar.hint:For visitors reading the chat in Arabic.`,
    },
    {
      id: 'widget-greeting',
      field: 'greeting',
      control: 'greeting',
      testId: 'greeting-default',
      dir: 'auto',
      lang: null,
      label: $localize`:@@widget.greeting.default:Default greeting`,
      hint: $localize`:@@widget.greeting.default.hint:Visitors whose language has no greeting of its own see this one.`,
    },
  ];

  /** A greeting box's description: its hint, and the server's refusal when it names this box. */
  protected describedBy(box: GreetingBox): string {
    return this.greetingProblem()?.field === box.field
      ? `${box.id}-hint ${box.id}-error`
      : `${box.id}-hint`;
  }

  /** The four corners, in the order the settings list them. */
  protected readonly corners: readonly LauncherPosition[] = ['Right', 'Left', 'Start', 'End'];

  /** Where a corner puts the launcher on a page in the reader's language, for its picture. */
  protected cornerSide(position: LauncherPosition): 'Left' | 'Right' {
    return physicalSide(position, this.language);
  }

  /** The chat's themes a visitor can see with the chosen theme setting. */
  protected readonly chatThemes = computed<readonly ChatTheme[]>(() => {
    const theme = this.values().theme;

    return theme === 'Light' ? ['light'] : theme === 'Dark' ? ['dark'] : ['light', 'dark'];
  });

  /**
   * Whether the colour may stand in for emerald inside the chat, per theme
   * (`accent-color.ts`). An invalid colour is judged as the default, as the
   * widget would paint it.
   */
  protected readonly chatChecks = computed(() => {
    const accent = this.accent().accent;

    return this.chatThemes().map((theme) => ({ theme, ...checkChatBrand(accent, theme) }));
  });

  /** The themes, among those visitors can see, where the chat keeps emerald. */
  protected readonly emeraldIn = computed<'light' | 'dark' | 'both' | null>(() => {
    const fallback = this.chatChecks().filter((check) => !check.used);

    if (fallback.length === 0) {
      return null;
    }

    return fallback.length === 2 ? 'both' : fallback[0].theme;
  });

  /** A greeting the server refused, by the field it named, shown beside that field. */
  protected readonly greetingProblem = computed(() => {
    const failure = this.saveFailure();

    return failure?.code?.startsWith('website-chat.greeting.') && failure.field
      ? { field: failure.field, message: failure.message }
      : null;
  });

  /** `Start` and `End` drawn as they would be on a page in the reader's language. */
  protected readonly previewSide = computed(() =>
    physicalSide(this.values().launcherPosition, this.language),
  );

  /** Heads the preview, as it heads the chat visitors see. */
  protected readonly workspaceName = computed(() => this.workspace.tenant()?.name ?? '');
  protected readonly previewTheme = computed(() => this.values().theme);

  protected readonly launcherLabel = computed(() =>
    this.agentName()
      ? $localize`:@@visitor.launcher.named:Chat with ${this.agentName()}:agent:`
      : $localize`:@@visitor.launcher:Open chat`,
  );

  /** A contrast ratio as people write it: one decimal, Western digits. */
  protected ratio(value: number): string {
    return `${formatNumber(this.language, Math.floor(value * 10) / 10, {
      minimumFractionDigits: 1,
      maximumFractionDigits: 1,
    })}:1`;
  }

  protected readonly boundary = BOUNDARY_CONTRAST;

  /** The Remove button's name, so a list of identical "Remove" buttons is told apart. */
  protected removeLabel(origin: string): string {
    return $localize`:@@widget.origins.removeLabel:Remove ${origin}:origin:`;
  }

  /** One new website at a time, so each is checked as it is added. */
  protected readonly originForm = new FormGroup({
    origin: new FormControl('', { nonNullable: true }),
  });

  ngOnInit(): void {
    void this.load();
  }

  /**
   * Re-reads the configuration after the page changed the endpoint elsewhere.
   *
   * <strong>It refuses to discard unsaved edits.</strong> Publishing the link
   * bumps the same version this section holds, so a silent reload is right when
   * nothing has been typed and wrong the moment something has. When there are
   * edits it says so and leaves them alone; the save will then be refused with a
   * `412` and the reader decides.
   */
  refresh(): void {
    if (this.hasUnsavedChanges()) {
      this.stale.set(true);

      return;
    }

    void this.load();
  }

  /**
   * Whether anything on this section differs from what the server holds: the
   * appearance form, or the list of websites (compared as lists, so removing a
   * website and adding it back is not a change).
   */
  hasUnsavedChanges(): boolean {
    const saved = this.configuration()?.allowedOrigins ?? [];
    const current = this.origins();

    return (
      this.form.dirty ||
      current.length !== saved.length ||
      current.some((origin, index) => origin !== saved[index])
    );
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.failure.set(null);
    this.stale.set(false);

    try {
      const read = await firstValueFrom(this.api.getWidget(tenantId, this.webChatEndpointId()));

      this.adopt(read.value, read.etag);
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * Adds one website to the list being edited.
   *
   * Checked and canonicalised here so the reader sees the exact value that will
   * be stored — `HTTPS://Example.COM:443` and `https://example.com` are one
   * website, and showing them as two would invite somebody to "remove" the one
   * that was never there.
   */
  protected addOrigin(): void {
    if (!this.canManage() || this.originsFull()) {
      return;
    }

    const checked = checkOrigin(this.originForm.controls.origin.value);

    if ('problem' in checked) {
      this.originProblem.set(checked.problem);

      return;
    }

    if (this.origins().includes(checked.origin)) {
      this.originProblem.set(
        $localize`:@@widget.origins.duplicate:That website is already on the list.`,
      );

      return;
    }

    this.originProblem.set(null);
    this.origins.update((current) => [...current, checked.origin]);
    this.originForm.controls.origin.setValue('');
    this.savedAt.set(false);
  }

  /**
   * Takes one website off the list being edited.
   *
   * Nothing is revoked until the section is saved, which is why the button below
   * says what saving will do rather than pretending the removal has happened.
   */
  protected removeOrigin(origin: string): void {
    if (!this.canManage()) {
      return;
    }

    this.origins.update((current) => current.filter((existing) => existing !== origin));
    this.originProblem.set(null);
    this.savedAt.set(false);
  }

  /** Sends the whole intended configuration, if the endpoint is still at this version. */
  protected async save(): Promise<void> {
    const tenantId = this.workspace.tenantId();
    const expected = this.version();

    if (!tenantId || !expected || this.saving() || !this.canManage() || this.form.invalid) {
      return;
    }

    this.saving.set(true);
    this.saveFailure.set(null);
    this.savedAt.set(false);
    this.stale.set(false);

    const greeting = this.form.controls.greeting.value.trim();
    const greetingEn = this.form.controls.greetingEn.value.trim();
    const greetingAr = this.form.controls.greetingAr.value.trim();

    try {
      const updated = await firstValueFrom(
        this.api.updateWidget(
          tenantId,
          this.webChatEndpointId(),
          {
            greeting: greeting.length > 0 ? greeting : null,
            // Both, always: an empty one clears it.
            greetings: { en: greetingEn || null, ar: greetingAr || null },
            accentColor: this.form.controls.accentColor.value,
            launcherPosition: this.form.controls.launcherPosition.value,
            theme: this.form.controls.theme.value,
            allowedOrigins: this.origins(),
          },
          expected,
        ),
      );

      this.adopt(updated.value, updated.etag);
      this.savedAt.set(true);
      this.saved.emit();
    } catch (error) {
      const failure = toApiError(error);

      // A stale write is not an error beside a button: the endpoint moved
      // underneath this section. What the reader typed is kept exactly as it is,
      // and re-reading is an explicit choice, because reloading would silently
      // throw away an allowlist somebody was halfway through changing.
      if (failure.kind === 'stale') {
        this.stale.set(true);
      } else {
        this.saveFailure.set(failure);
        this.focusRefusedGreeting(failure.field);
      }
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * Puts the install snippet, or the loader address, on the clipboard.
   *
   * Both are shown as text as well, because the clipboard can be refused by
   * permission or by an insecure context.
   */
  protected async copy(what: 'snippet' | 'loader'): Promise<void> {
    const current = this.configuration();

    if (!current) {
      return;
    }

    try {
      await navigator.clipboard.writeText(
        what === 'snippet' ? current.installSnippet : current.loaderUrl,
      );

      this.copied.set(what);
    } catch {
      this.copied.set(null);
    }
  }

  /** Puts the cursor in the greeting the server refused, so the reader can fix it. */
  private focusRefusedGreeting(field: string | undefined): void {
    const id = field ? GREETING_FIELD_IDS[field] : undefined;

    if (id) {
      queueMicrotask(() => document.getElementById(id)?.focus());
    }
  }

  /** Takes on a configuration the server has confirmed, discarding local edits. */
  private adopt(configuration: WebChatWidgetConfiguration, etag: string): void {
    this.configuration.set(configuration);
    this.version.set(etag);
    this.origins.set([...configuration.allowedOrigins]);
    this.originProblem.set(null);
    this.originForm.controls.origin.setValue('');

    this.form.setValue({
      greeting: configuration.widget.greeting ?? '',
      greetingEn: configuration.widget.greetings?.en ?? '',
      greetingAr: configuration.widget.greetings?.ar ?? '',
      accentColor: configuration.widget.accentColor,
      launcherPosition: configuration.widget.launcherPosition,
      theme: configuration.widget.theme,
    });

    this.form.markAsPristine();

    if (!this.canManage()) {
      // Read-only for somebody who may look but not change. The server refuses
      // the write regardless; this is so the screen does not invite an action it
      // knows will be refused.
      this.form.disable();
      this.originForm.disable();
    }
  }
}
