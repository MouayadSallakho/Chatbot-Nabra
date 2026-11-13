import type { AppLanguage } from '@app/core/i18n/language';
import { pluralMessage } from '@app/core/i18n/plural';
import type { KnowledgeFileType, KnowledgeIndexAttempt } from './knowledge-api';

/**
 * Knowledge's words, written once (Phase 36.8).
 *
 * <strong>The states are the API's, and only the API's.</strong> The server
 * publishes three readiness words: `Ready` (said "Indexed"), `NeedsIndex` and
 * `Disabled`. Since Phase 38.3 the newest index attempt adds what the document
 * cannot say by itself: it is being indexed now, or the last attempt failed or
 * stopped. The editor also says "Indexing failed" for a request that failed
 * while the reader was there.
 *
 * Arabic adjectives agree with مستند (document, masculine).
 */
export type KnowledgeDisplayState =
  'Ready' | 'NeedsIndex' | 'Disabled' | 'Failed' | 'Indexing' | 'Stopped';

const STATE_WORDS: Readonly<Record<KnowledgeDisplayState, string>> = {
  Ready: $localize`:@@knowledge.state.ready:Indexed`,
  NeedsIndex: $localize`:@@knowledge.state.needsIndex:Needs index`,
  Disabled: $localize`:@@knowledge.state.disabled:Disabled`,
  Failed: $localize`:@@knowledge.state.failed:Indexing failed`,
  Indexing: $localize`:@@knowledge.state.indexing:Indexing now`,
  Stopped: $localize`:@@knowledge.state.stopped:Indexing stopped`,
};

/** The reasons an index attempt fails, by the server's stable `failureCode`. */
const FAILURE_REASONS: Readonly<Record<string, string>> = {
  provider_unavailable: $localize`:@@knowledge.failure.provider_unavailable:The AI provider could not be reached.`,
  provider_timeout: $localize`:@@knowledge.failure.provider_timeout:The AI provider took too long to answer.`,
  provider_response_invalid: $localize`:@@knowledge.failure.provider_response_invalid:The AI provider's answer could not be used.`,
  quota_exhausted: $localize`:@@knowledge.failure.quota_exhausted:This workspace has used all the AI usage its plan allows.`,
  document_changed: $localize`:@@knowledge.failure.document_changed:The document changed while it was being indexed.`,
  cancelled: $localize`:@@knowledge.failure.cancelled:Indexing was cancelled.`,
  interrupted: $localize`:@@knowledge.failure.interrupted:Indexing was interrupted before it finished.`,
  // The server's own deadline (Phase 38.7a, BA-10): the attempt no longer depends
  // on the tab that started it, so a slow provider ends it here instead.
  timed_out: $localize`:@@knowledge.failure.timed_out:Indexing took longer than allowed. Nothing was indexed.`,
  indexing_error: $localize`:@@knowledge.failure.indexing_error:Something went wrong while indexing.`,
};

/** A failed attempt's reason; a code this build does not know gets the general one. */
export function indexFailureReason(failureCode: string | null): string {
  return (
    (failureCode !== null ? FAILURE_REASONS[failureCode] : null) ??
    FAILURE_REASONS['indexing_error']
  );
}

/**
 * The state to show: the document's readiness, and what its newest attempt adds.
 *
 * A disabled document says so whatever was tried. An attempt running now is news
 * even for an indexed document. Failure and an unfinished attempt only matter
 * while the document still needs an index: one that is `Ready` is answering.
 */
export function knowledgeDisplayState(
  readiness: string,
  attempt: KnowledgeIndexAttempt | null | undefined,
): string {
  if (readiness === 'Disabled') {
    return readiness;
  }

  if (attempt?.status === 'Started') {
    return 'Indexing';
  }

  if (readiness !== 'NeedsIndex') {
    return readiness;
  }

  switch (attempt?.status) {
    case 'Failed':
      return 'Failed';
    case 'Unknown':
      return 'Stopped';
    default:
      return readiness;
  }
}

/** The line a list row adds under a failed or unfinished state, or null. */
export function indexAttemptNote(
  state: string,
  attempt: KnowledgeIndexAttempt | null | undefined,
): string | null {
  if (state === 'Failed') {
    return indexFailureReason(attempt?.failureCode ?? null);
  }

  return state === 'Stopped'
    ? $localize`:@@knowledge.attempt.stopped:It stopped before it finished.`
    : null;
}

/** A state in words; an unknown value is shown as the server sent it. */
export function knowledgeStateWord(state: string): string {
  return STATE_WORDS[state as KnowledgeDisplayState] ?? state;
}

/** The badge for a state: each has its own glyph as well as its word. */
export function knowledgeStateTone(state: string): string {
  switch (state) {
    case 'Ready':
      return 'badge-success';
    case 'NeedsIndex':
      return 'badge-warning';
    case 'Failed':
      return 'badge-danger';
    case 'Indexing':
      return 'badge-info';
    case 'Stopped':
      return 'badge-warning';
    default:
      return 'badge-neutral';
  }
}

/** The format a document was read from, or "Written" when somebody typed it. */
export function knowledgeTypeWord(fileType: KnowledgeFileType | string | null): string {
  switch (fileType) {
    case 'Pdf':
      return 'PDF';
    case 'Docx':
      return $localize`:@@knowledge.type.docx:Word document`;
    case 'Text':
      return $localize`:@@knowledge.type.text:Text file`;
    case 'Csv':
      return 'CSV';
    case null:
      return $localize`:@@knowledge.type.written:Written`;
    default:
      return String(fileType);
  }
}

/**
 * The size of a document's text, as a full sentence per plural category.
 *
 * What the editor says about the saved text. The list says the source file's
 * size instead (`sourceByteLength`).
 */
export function characterCount(language: AppLanguage, count: number, formatted: string): string {
  return pluralMessage(language, count, {
    zero: $localize`:@@knowledge.chars.zero:${formatted}:count: characters`,
    one: $localize`:@@knowledge.chars.one:${formatted}:count: character`,
    two: $localize`:@@knowledge.chars.two:${formatted}:count: characters`,
    few: $localize`:@@knowledge.chars.few:${formatted}:count: characters`,
    many: $localize`:@@knowledge.chars.many:${formatted}:count: characters`,
    other: $localize`:@@knowledge.chars.other:${formatted}:count: characters`,
  });
}

/** The list's Size column, for a document somebody typed rather than uploaded. */
export const TYPED_TEXT = $localize`:@@knowledge.size.typed:Typed text`;

/** The formats an upload accepts, as a reader says them, in the policy's order. */
export function acceptedFormats(): string {
  return $localize`:@@knowledge.formats:PDF, Word (.docx), plain text or CSV`;
}
