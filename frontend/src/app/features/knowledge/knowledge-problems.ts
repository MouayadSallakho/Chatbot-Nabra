import { formatNumber } from '@app/core/i18n/locale-format';
import type {
  ProblemCatalogue,
  ProblemFacts,
  ProblemWording,
} from '@app/core/problems/problem-words';
import { fileSize, maxSizeLabel, tooLargeSentence, unsupportedSentence } from './knowledge-file';

/** For a bound the server named without saying what it was: it always says, today. */
const OVER_A_LIMIT = $localize`:@@problem.knowledge.file.overLimit:This file is past one of the limits a document may have. Split it and upload the part that matters.`;

/** A sentence with the broken bound in it, in the reader's digits. */
function withLimit(sentence: (limit: string) => string): ProblemWording {
  return ({ limit, language }: ProblemFacts) =>
    limit === undefined ? OVER_A_LIMIT : sentence(formatNumber(language, limit));
}

/**
 * The knowledge screens' refusals, by the server's stable `code`.
 *
 * Registered by the knowledge routes and the agents' (whose editor hosts the
 * knowledge section), so the words arrive with the feature and cost the startup
 * JavaScript nothing. A code that is not here keeps today's generic wording, and
 * the English `detail` is never what an Arabic reader sees.
 *
 * The bounds read "more than the limit, which is N" in Arabic so that one sentence
 * serves every number: a noun counted by N would change form with it.
 *
 * `knowledge.file.archive_too_large` names no bound, because the server's `limit`
 * may be a count of parts or a number of bytes and does not say which.
 */
export const KNOWLEDGE_PROBLEM_WORDS: ProblemCatalogue = {
  'knowledge.file.multipart_required': $localize`:@@problem.knowledge.file.multipart_required:This file could not be sent. Reload the page and try again.`,
  'knowledge.file.required': $localize`:@@problem.knowledge.file.required:Choose a file before uploading.`,
  'knowledge.file.too_large': ({ limit, language }) =>
    tooLargeSentence(limit === undefined ? maxSizeLabel(language) : fileSize(limit, language)),
  'knowledge.file.unsupported_type': () => unsupportedSentence(),
  'knowledge.file.type_mismatch': $localize`:@@problem.knowledge.file.type_mismatch:This file's content does not match its type. Save it again in the right format and upload it.`,
  'knowledge.file.empty': $localize`:@@knowledge.file.empty:That file is empty. Choose a file with something in it.`,
  'knowledge.file.not_utf8': $localize`:@@problem.knowledge.file.not_utf8:This text file is not saved as UTF-8. Save it as UTF-8 and upload it again.`,
  'knowledge.file.damaged': $localize`:@@problem.knowledge.file.damaged:This file could not be read. It may be damaged. Try saving it again.`,
  'knowledge.file.encrypted': $localize`:@@problem.knowledge.file.encrypted:This PDF is protected with a password. Remove the password and upload it again.`,
  'knowledge.file.no_text': $localize`:@@problem.knowledge.file.no_text:No text could be found in this file. Scanned pages and images are not read.`,
  'knowledge.file.header_required': $localize`:@@problem.knowledge.file.header_required:This CSV file needs a first row that names its columns.`,
  'knowledge.file.too_many_pages': withLimit(
    (limit) =>
      $localize`:@@problem.knowledge.file.too_many_pages:The file has more than ${limit}:limit: pages. Split it and upload the part that matters.`,
  ),
  'knowledge.file.too_many_characters': withLimit(
    (limit) =>
      $localize`:@@problem.knowledge.file.too_many_characters:The file holds more than ${limit}:limit: characters of text. Split it and upload the part that matters.`,
  ),
  'knowledge.file.too_many_rows': withLimit(
    (limit) =>
      $localize`:@@problem.knowledge.file.too_many_rows:The file has more than ${limit}:limit: rows. Split it and upload the part that matters.`,
  ),
  'knowledge.file.too_many_columns': withLimit(
    (limit) =>
      $localize`:@@problem.knowledge.file.too_many_columns:The file has more than ${limit}:limit: columns. Remove the ones you do not need and upload it again.`,
  ),
  'knowledge.file.cell_too_long': withLimit(
    (limit) =>
      $localize`:@@problem.knowledge.file.cell_too_long:A cell in the file is longer than ${limit}:limit: characters. Shorten it and upload the file again.`,
  ),
  'knowledge.file.archive_too_large': $localize`:@@problem.knowledge.file.archive_too_large:This Word document is too large once opened. Split it, or save a simpler copy.`,
  'knowledge.file.name.required': $localize`:@@problem.knowledge.file.name.required:This file has no name. Rename it and upload it again.`,
  'knowledge.file.name.unreadable': $localize`:@@problem.knowledge.file.name.unreadable:This file's name could not be read. Rename it and upload it again.`,
  'knowledge.file.name.too_long': $localize`:@@problem.knowledge.file.name.too_long:This file's name is too long. Shorten it and upload it again.`,

  'knowledge.title.required': $localize`:@@problem.knowledge.title.required:Give this document a title.`,
  'knowledge.title.too_long': $localize`:@@problem.knowledge.title.too_long:This title is too long. Shorten it and save again.`,
  'knowledge.title.control_characters': $localize`:@@problem.knowledge.title.control_characters:A title has to be one line of plain text.`,
  'knowledge.content.required': $localize`:@@problem.knowledge.content.required:Write something in this document.`,
  'knowledge.content.too_long': $localize`:@@problem.knowledge.content.too_long:This document is too long. Shorten it and save again.`,
  'knowledge.content.control_characters': $localize`:@@problem.knowledge.content.control_characters:This text has characters that cannot be saved. Paste it as plain text and try again.`,
  'knowledge.document.already_enabled': $localize`:@@problem.knowledge.document.already_enabled:This document is already in use.`,
  'knowledge.document.already_disabled': $localize`:@@problem.knowledge.document.already_disabled:This document is already out of use.`,
  'knowledge.indexing.in_progress': $localize`:@@problem.knowledge.indexing.in_progress:This document is being indexed right now, so nothing was sent. Reload in a moment to see how it ended.`,
  'knowledge.index.too_many_chunks': $localize`:@@problem.knowledge.index.too_many_chunks:This document is too long to index. Split it into smaller documents.`,
  'knowledge.source.missing': $localize`:@@problem.knowledge.source.missing:This document was written here, so there is no file to replace.`,
  'knowledge.assignment.document_unknown': $localize`:@@problem.knowledge.assignment.document_unknown:One of these documents is no longer in this workspace. Reload the page and choose again.`,
  'knowledge.assignment.too_many': $localize`:@@problem.knowledge.assignment.too_many:An agent cannot draw on this many documents. Remove some and save again.`,
  'knowledge.search.question.required': $localize`:@@problem.knowledge.search.question.required:Write a question to test.`,
  'knowledge.search.question.too_long': $localize`:@@problem.knowledge.search.question.too_long:This question is too long. Shorten it and try again.`,
  'entitlement.knowledgedocuments.exhausted': $localize`:@@problem.entitlement.knowledgedocuments.exhausted:This workspace has reached the number of knowledge documents its plan allows. Nothing was saved.`,
};
