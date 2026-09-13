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
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import {
  WebChatAdminApi,
  WebChatWidgetConfiguration,
} from '@app/features/channels/website-chat-api';
import { checkOrigin } from '../allowed-origin';

/** The server's bound on a greeting, so the box can say so before sending. */
const MAX_GREETING_LENGTH = 300;

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
 */
@Component({
  selector: 'app-website-chat-widget',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './website-chat-widget.html',
  styleUrl: './website-chat-widget.scss',
})
export class WebsiteChatWidget implements OnInit {
  private readonly api = inject(WebChatAdminApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);

  /** The endpoint being configured. */
  readonly webChatEndpointId = input.required<string>();

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
    accentColor: new FormControl('#2563eb', {
      nonNullable: true,
      validators: [Validators.required, Validators.pattern(ACCENT_PATTERN)],
    }),
    launcherPosition: new FormControl<'Left' | 'Right'>('Right', { nonNullable: true }),
    theme: new FormControl<'System' | 'Light' | 'Dark'>('System', { nonNullable: true }),
  });

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
    if (
      this.form.dirty ||
      this.origins().length !== (this.configuration()?.allowedOrigins.length ?? 0)
    ) {
      this.stale.set(true);

      return;
    }

    void this.load();
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
      this.originProblem.set('That website is already on the list.');

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

    try {
      const updated = await firstValueFrom(
        this.api.updateWidget(
          tenantId,
          this.webChatEndpointId(),
          {
            greeting: greeting.length > 0 ? greeting : null,
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

  /** Takes on a configuration the server has confirmed, discarding local edits. */
  private adopt(configuration: WebChatWidgetConfiguration, etag: string): void {
    this.configuration.set(configuration);
    this.version.set(etag);
    this.origins.set([...configuration.allowedOrigins]);
    this.originProblem.set(null);
    this.originForm.controls.origin.setValue('');

    this.form.setValue({
      greeting: configuration.widget.greeting ?? '',
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
