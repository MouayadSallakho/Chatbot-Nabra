import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiError, toApiError } from '@app/core/api/api-error';
import { ACCEPT_INVITATION_PATH } from '@app/core/invitations/invitation-handoff';
import { PermissionService } from '@app/core/authorization/permission-service';
import { WorkspaceContextService } from '@app/core/workspace/workspace-context-service';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { APP_LANGUAGES, AppLanguage, LANGUAGE_ENDONYMS } from '@app/core/i18n/language';
import { EmptyState } from '@app/shared/empty-state/empty-state';
import { Icon } from '@app/shared/icon/icon';
import { PageHeader } from '@app/shared/page-header/page-header';
import { permissionCount } from '@app/features/roles/permission-words';
import { AssignableRole, TeamApi } from '../team-api';
import { roleList, rolesSelected } from '../team-words';
import { Check } from '@app/shared/choice/choice';

/**
 * Inviting somebody to the workspace.
 *
 * <strong>The link is shown once and then it is gone.</strong> The token exists
 * in the response to creating the invitation and nowhere else — not in the
 * database, not in any later read, and not in browser storage. It lives in this
 * component's memory until the reader navigates away, which is exactly as long
 * as they need it to copy the link. If they lose it, the invitation is revoked
 * and a new one issued; there is no recovery, because a recoverable secret is
 * one that is stored somewhere.
 *
 * <strong>Inviting and granting access are separate permissions.</strong>
 * Somebody with `members.invite` alone can still bring a colleague in — they
 * arrive able to see the workspace and do nothing — and the form says so rather
 * than hiding itself. That is a genuinely useful thing to allow, and it keeps
 * people from asking for the broader permission just to add a teammate.
 */
@Component({
  selector: 'app-invite-member',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, PageHeader, EmptyState, Icon, Check],
  templateUrl: './invite-member.html',
  styleUrl: './invite-member.scss',
})
export class InviteMember {
  private readonly api = inject(TeamApi);
  private readonly workspace = inject(WorkspaceContextService);
  private readonly permissions = inject(PermissionService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly locale = inject(LocaleFormat);

  protected readonly loading = signal(true);
  protected readonly loadFailure = signal<ApiError | null>(null);
  protected readonly submitting = signal(false);
  protected readonly failure = signal<ApiError | null>(null);
  protected readonly assignable = signal<readonly AssignableRole[]>([]);

  /**
   * The finished invitation, and its one copy of the link.
   *
   * Held in a signal on this component, which means it is discarded the moment
   * the reader leaves. Nothing writes it anywhere that survives that.
   */
  protected readonly created = signal<{
    email: string;
    link: string;
    roles: string;
    language: AppLanguage;
  } | null>(null);
  protected readonly copied = signal(false);

  /** Set when the clipboard refused; the link is on screen to select by hand. */
  protected readonly copyFailed = signal(false);

  private readonly selected = signal<ReadonlySet<string>>(new Set());
  private readonly submitAttempted = signal(false);

  protected readonly canInvite = computed(() => this.permissions.has('members.invite'));
  protected readonly canAssign = computed(() => this.permissions.has('members.roles.manage'));

  protected readonly selectedCount = computed(() => this.selected().size);

  protected readonly selectedText = computed(() =>
    rolesSelected(this.selected().size, this.locale.language),
  );

  protected roleCount(role: AssignableRole): string {
    return permissionCount(role.permissionCount, this.locale.language);
  }

  protected onToggle(roleId: string, event: Event): void {
    this.toggle(roleId, (event.target as HTMLInputElement).checked);
  }

  /** The languages an invitation email can be written in, each named in itself. */
  protected readonly languages = APP_LANGUAGES;
  protected readonly endonyms = LANGUAGE_ENDONYMS;

  protected readonly form = new FormGroup({
    email: new FormControl('', {
      nonNullable: true,
      validators: [Validators.required, Validators.email],
    }),

    /**
     * The invitation email's language. It starts as the language the sender is
     * using Nabra in, and is always sent (the owner's decision, 38.0 §4.3 #4), so
     * the server's own default never decides it silently. Resetting the form
     * returns it to that language.
     */
    language: new FormControl<AppLanguage>(this.locale.language, { nonNullable: true }),
  });

  constructor() {
    void this.load();
  }

  protected isTicked(roleId: string): boolean {
    return this.selected().has(roleId);
  }

  protected toggle(roleId: string, ticked: boolean): void {
    this.selected.update((current) => {
      const next = new Set(current);

      if (ticked) {
        next.add(roleId);
      } else {
        next.delete(roleId);
      }

      return next;
    });
  }

  protected showEmailError(): boolean {
    const control = this.form.controls.email;

    return control.invalid && (control.touched || this.submitAttempted());
  }

  /** Says what is wrong: an empty field and a malformed address are different mistakes. */
  protected emailErrorText(): string {
    return this.form.controls.email.hasError('required')
      ? $localize`:@@team.invite.email.required:Enter the email address to invite.`
      : $localize`:@@team.invite.email.invalid:Enter a complete email address, such as name@example.com.`;
  }

  protected async load(): Promise<void> {
    const tenantId = this.workspace.tenantId();

    if (!tenantId || !this.canInvite()) {
      this.loading.set(false);

      return;
    }

    this.loading.set(true);
    this.loadFailure.set(null);

    try {
      // Only somebody who may hand out roles has any use for the list, and
      // asking for it without the permission would produce a 403 in the console
      // for no reason.
      const roles = this.canAssign()
        ? await firstValueFrom(this.api.assignableRoles(tenantId))
        : [];

      this.assignable.set(roles);
    } catch (error) {
      this.loadFailure.set(toApiError(error));
    } finally {
      this.loading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    this.submitAttempted.set(true);

    const tenantId = this.workspace.tenantId();

    if (!tenantId || this.form.invalid || this.submitting()) {
      this.form.markAllAsTouched();

      if (this.form.invalid) {
        this.focusAfterRender('#invite-email');
      }

      return;
    }

    this.submitting.set(true);
    this.failure.set(null);

    try {
      const language = this.form.controls.language.value;
      const invitation = await firstValueFrom(
        this.api.invite(
          tenantId,
          this.form.controls.email.value.trim(),
          [...this.selected()],
          language,
        ),
      );

      // Built here rather than by the server, because the shape of the link is a
      // property of this client's routing. The token goes in the fragment: a
      // fragment is never sent to the server and is not forwarded in a Referer
      // header, which a query string is.
      this.created.set({
        email: invitation.invitation.email,
        roles: roleList(invitation.invitation.roles, this.locale.language),
        language,
        link:
          `${window.location.origin}/${ACCEPT_INVITATION_PATH}` +
          `#token=${encodeURIComponent(invitation.token)}`,
      });
      // The form that held focus has just been replaced by the result.
      this.focusAfterRender('#invite-created-heading');
    } catch (error) {
      this.failure.set(toApiError(error));
    } finally {
      this.submitting.set(false);
    }
  }

  /**
   * Puts the link on the clipboard.
   *
   * The reader is told it worked, because a copy button that does nothing
   * visible leaves somebody pasting into a message to find out. A failure is
   * reported rather than swallowed: the clipboard can be refused by permission
   * or by an insecure context, and the link is still on screen to select by
   * hand.
   */
  protected async copy(): Promise<void> {
    const invitation = this.created();

    if (!invitation) {
      return;
    }

    try {
      await navigator.clipboard.writeText(invitation.link);

      this.copied.set(true);
      this.copyFailed.set(false);
    } catch {
      this.copied.set(false);
      this.copyFailed.set(true);
    }
  }

  /** Clears the finished invitation so another can be composed. */
  protected inviteAnother(): void {
    this.created.set(null);
    this.copied.set(false);
    this.copyFailed.set(false);
    this.submitAttempted.set(false);
    this.selected.set(new Set());
    this.form.reset();
    this.focusAfterRender('#invite-email');
  }

  private focusAfterRender(selector: string): void {
    afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>(selector)?.focus(), {
      injector: this.injector,
    });
  }
}
