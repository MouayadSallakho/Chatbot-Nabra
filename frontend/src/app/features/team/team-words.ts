import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { InvitationStatus } from './team-api';

/**
 * Team's words, written once (Phase 36.10).
 *
 * Invitation states are the server's (`Pending`, `Accepted`, `Revoked`,
 * `Expired`); each has a word and a marker of its own (an "i" for one still
 * waiting, a tick for accepted, a filled dot for revoked, a hollow ring for
 * expired), so none is told apart by colour.
 */
const STATUS_WORDS: Readonly<Record<InvitationStatus, string>> = {
  Pending: $localize`:@@team.invitation.status.pending:Waiting`,
  Accepted: $localize`:@@team.invitation.status.accepted:Accepted`,
  Revoked: $localize`:@@team.invitation.status.revoked:Revoked`,
  Expired: $localize`:@@team.invitation.status.expired:Expired`,
};

/** An invitation's state in words; an unknown value is shown as the server sent it. */
export function invitationStatusWord(status: string): string {
  return STATUS_WORDS[status as InvitationStatus] ?? status;
}

export function invitationStatusTone(status: string): string {
  switch (status) {
    case 'Pending':
      return 'badge-info';
    case 'Accepted':
      return 'badge-success';
    case 'Expired':
      return 'badge-neutral badge-draft';
    default:
      return 'badge-neutral';
  }
}

/**
 * Role names as one phrase in the reader's language ("Support Agent and
 * Analyst", «Support Agent وAnalyst»). The names themselves are the
 * workspace's own words and are never translated.
 */
export function roleList(names: readonly string[], language: AppLanguage): string {
  return new Intl.ListFormat(language, { type: 'conjunction', style: 'long' }).format(names);
}

/** "3 roles selected" for the invite form and the member editor. */
export function rolesSelected(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@team.rolesSelected.zero:No roles selected: joins with no access`,
    one: $localize`:@@team.rolesSelected.one:${n}:count: role selected`,
    two: $localize`:@@team.rolesSelected.two:${n}:count: roles selected`,
    few: $localize`:@@team.rolesSelected.few:${n}:count: roles selected`,
    many: $localize`:@@team.rolesSelected.many:${n}:count: roles selected`,
    other: $localize`:@@team.rolesSelected.other:${n}:count: roles selected`,
  });
}

/** "2 invitations are waiting", for the section summary. */
export function waitingSummary(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@team.waiting.zero:No invitations are waiting.`,
    one: $localize`:@@team.waiting.one:${n}:count: invitation is waiting to be accepted.`,
    two: $localize`:@@team.waiting.two:${n}:count: invitations are waiting to be accepted.`,
    few: $localize`:@@team.waiting.few:${n}:count: invitations are waiting to be accepted.`,
    many: $localize`:@@team.waiting.many:${n}:count: invitations are waiting to be accepted.`,
    other: $localize`:@@team.waiting.other:${n}:count: invitations are waiting to be accepted.`,
  });
}

/** "Expires in 6 days" / "Expires today": when a waiting invitation's link stops working. */
export function expiresIn(iso: string, now: number, language: AppLanguage): string {
  const days = Math.ceil((new Date(iso).getTime() - now) / 86_400_000);

  if (days <= 0) {
    return $localize`:@@team.expires.today:Expires today`;
  }

  const n = formatNumber(language, days);

  return pluralMessage(language, days, {
    zero: $localize`:@@team.expires.zero:Expires today`,
    one: $localize`:@@team.expires.one:Expires in ${n}:count: day`,
    two: $localize`:@@team.expires.two:Expires in ${n}:count: days`,
    few: $localize`:@@team.expires.few:Expires in ${n}:count: days`,
    many: $localize`:@@team.expires.many:Expires in ${n}:count: days`,
    other: $localize`:@@team.expires.other:Expires in ${n}:count: days`,
  });
}
