import type { ApiError } from '@app/core/api/api-error';
import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { sizeLabel } from '@app/features/reports/reports-words';
import type { KnowledgeFileType } from './knowledge-api';
import { KNOWLEDGE_FILE_LIMITS } from './knowledge-limits';
import { acceptedFormats } from './knowledge-words';

/**
 * The courtesy checks on a chosen file, and the words for what went wrong
 * (Phase 36.8, shared by the upload page and the editor's "Replace file").
 *
 * <strong>These checks are not the enforcement.</strong> The backend decides
 * what a file really is by reading its signature, and refuses on its own terms
 * whatever happens here. They exist so somebody learns before waiting for an
 * upload, and the limits come from `knowledge-limits.ts`, which mirrors the
 * backend's policy.
 */

const EXTENSIONS: Readonly<Record<string, KnowledgeFileType>> = {
  '.pdf': 'Pdf',
  '.docx': 'Docx',
  '.txt': 'Text',
  '.csv': 'Csv',
};

/** The format a file's name claims, or null for one the policy does not take. */
export function claimedFileType(name: string): KnowledgeFileType | null {
  const lowered = name.toLowerCase();
  const extension = Object.keys(EXTENSIONS).find((candidate) => lowered.endsWith(candidate));

  return extension ? EXTENSIONS[extension] : null;
}

/**
 * A byte count in the reader's language ("457 bytes", "1.4 MB"). Under a
 * kilobyte the unit is written out, because the short form has no plural
 * ("457 byte").
 */
export function fileSize(bytes: number, language: AppLanguage): string {
  if (bytes < 1024) {
    return formatNumber(language, bytes, { style: 'unit', unit: 'byte', unitDisplay: 'long' });
  }

  return sizeLabel(bytes, language);
}

/** The upload limit, said the way the page says it ("10 MB"). */
export function maxSizeLabel(language: AppLanguage): string {
  return formatNumber(language, KNOWLEDGE_FILE_LIMITS.maxMegabytes, {
    style: 'unit',
    unit: 'megabyte',
    unitDisplay: 'short',
  });
}

/** Why a chosen file is hopeless before it is ever sent, or null when it may be. */
export function fileRefusal(file: File, language: AppLanguage): string | null {
  if (file.size === 0) {
    return $localize`:@@knowledge.file.empty:That file is empty. Choose a file with something in it.`;
  }

  if (file.size > KNOWLEDGE_FILE_LIMITS.maxBytes) {
    const size = fileSize(file.size, language);
    const max = maxSizeLabel(language);

    return $localize`:@@knowledge.file.tooLarge:That file is ${size}:size:, and files must be ${max}:max: or smaller. Split it, or upload the part that matters.`;
  }

  if (claimedFileType(file.name) === null) {
    const formats = acceptedFormats();

    return $localize`:@@knowledge.file.unsupported:That file is not one Nabra can read. Choose a ${formats}:formats: file.`;
  }

  return null;
}

/**
 * A server refusal of a file, in the reader's language.
 *
 * Too large and unsupported are said in our own words with the real limits
 * (the server's sentences are English only). A file whose content could not be
 * used (422) keeps the server's own sentence in English, which names the
 * reason (a scanned PDF, too many pages); in Arabic it gets one translated
 * sentence covering those reasons, because the server sends no stable code to
 * translate by. Everything else is the shared error copy.
 */
export function uploadFailure(error: ApiError, language: AppLanguage): string {
  switch (error.kind) {
    case 'tooLarge': {
      const max = maxSizeLabel(language);

      return $localize`:@@knowledge.upload.serverTooLarge:That file is larger than ${max}:max:, the most Nabra accepts. Nothing was saved.`;
    }
    case 'unsupportedMedia': {
      const formats = acceptedFormats();

      return $localize`:@@knowledge.upload.serverUnsupported:That file is not a ${formats}:formats: file, so it was not read. Nothing was saved.`;
    }
    case 'unprocessable':
      return language === 'en'
        ? error.message
        : $localize`:@@knowledge.upload.serverUnprocessable:No usable text could be read from that file. It may be a scanned PDF (pictures of text are not read), empty, damaged, or longer than a document may be.`;
    default:
      return error.message;
  }
}
