import type { AppLanguage } from '@app/core/i18n/language';
import { pluralMessage } from '@app/core/i18n/plural';
import type { KnowledgeFileType } from './knowledge-api';

/**
 * Knowledge's words, written once (Phase 36.8).
 *
 * <strong>The states are the API's, and only the API's.</strong> The server
 * publishes three: `Ready` (said "Indexed"), `NeedsIndex` and `Disabled`. A
 * fourth, "Indexing failed", is shown only on the editor, for an index
 * request that really failed while the reader was there: the API keeps no
 * failed state on the document (a failed attempt leaves it needing an index),
 * so a list can never say which documents failed before. That is a backend ask,
 * not something to guess from timestamps.
 *
 * Arabic adjectives agree with مستند (document, masculine).
 */
export type KnowledgeDisplayState = 'Ready' | 'NeedsIndex' | 'Disabled' | 'Failed';

const STATE_WORDS: Readonly<Record<KnowledgeDisplayState, string>> = {
  Ready: $localize`:@@knowledge.state.ready:Indexed`,
  NeedsIndex: $localize`:@@knowledge.state.needsIndex:Needs index`,
  Disabled: $localize`:@@knowledge.state.disabled:Disabled`,
  Failed: $localize`:@@knowledge.state.failed:Indexing failed`,
};

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
 * The list publishes the text's length in characters (not the source file's
 * bytes, which only the document itself carries), so that is what is said.
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

/** The formats an upload accepts, as a reader says them, in the policy's order. */
export function acceptedFormats(): string {
  return $localize`:@@knowledge.formats:PDF, Word (.docx), plain text or CSV`;
}
