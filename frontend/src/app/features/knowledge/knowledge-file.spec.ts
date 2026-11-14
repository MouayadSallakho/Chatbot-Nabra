import { ApiError } from '@app/core/api/api-error';
import { KNOWLEDGE_FILE_LIMITS } from './knowledge-limits';
import {
  claimedFileType,
  fileRefusal,
  fileSize,
  maxSizeLabel,
  uploadFailure,
} from './knowledge-file';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { KNOWLEDGE_PROBLEM_WORDS } from './knowledge-problems';
import {
  characterCount,
  knowledgeGlyph,
  knowledgeStateTone,
  knowledgeStateWord,
  knowledgeTypeWord,
} from './knowledge-words';

/**
 * The words and courtesy checks shared by the upload page and "Replace file".
 * The limits come from knowledge-limits.ts, which mirrors the backend policy.
 */
describe('knowledge file checks', () => {
  const file = (name: string, size: number): File => new File([new Uint8Array(size)], name);

  it('reads the format a name claims, in the policy only', () => {
    expect(claimedFileType('Policy.PDF')).toBe('Pdf');
    expect(claimedFileType('terms.docx')).toBe('Docx');
    expect(claimedFileType('notes.txt')).toBe('Text');
    expect(claimedFileType('prices.csv')).toBe('Csv');
    expect(claimedFileType('deck.pptx')).toBeNull();
    expect(claimedFileType('old.doc')).toBeNull();
  });

  it('says sizes in the unit a person uses, in both languages', () => {
    expect(fileSize(512, 'en')).toBe('512 bytes');
    expect(fileSize(2_048, 'en')).toBe('2 kB');
    expect(fileSize(1_572_864, 'en')).toBe('1.5 MB');
    expect(maxSizeLabel('en')).toBe(`${KNOWLEDGE_FILE_LIMITS.maxMegabytes} MB`);
    // Western digits in Arabic too (MASTER section 4).
    expect(maxSizeLabel('ar')).toContain('10');
  });

  it('refuses empty, oversized and unreadable files, and nothing else', () => {
    expect(fileRefusal(file('a.pdf', 0), 'en')).toContain('empty');
    expect(fileRefusal(file('a.pdf', KNOWLEDGE_FILE_LIMITS.maxBytes + 1), 'en')).toContain(
      '10 MB or smaller',
    );
    expect(fileRefusal(file('a.png', 10), 'en')).toContain('PDF, Word (.docx), plain text or CSV');
    expect(fileRefusal(file('a.pdf', KNOWLEDGE_FILE_LIMITS.maxBytes), 'en')).toBeNull();
  });

  it('says server refusals in its own words, keeping the specific 422 sentence in English', () => {
    const error = (kind: ApiError['kind'], message = 'server words'): ApiError => ({
      kind,
      status: 0,
      message,
    });

    expect(uploadFailure(error('tooLarge'), 'en')).toContain('larger than 10 MB');
    expect(uploadFailure(error('unsupportedMedia'), 'en')).toContain('so it was not read');
    expect(uploadFailure(error('unprocessable', 'No extractable text.'), 'en')).toBe(
      'No extractable text.',
    );
    // Arabic gets one translated sentence: the server sends no code to translate by.
    expect(uploadFailure(error('unprocessable', 'No extractable text.'), 'ar')).not.toBe(
      'No extractable text.',
    );
    expect(uploadFailure(error('offline', 'offline copy'), 'en')).toBe('offline copy');
  });

  it('says a coded refusal in the catalogue words, whatever its status', () => {
    registerProblemWords(KNOWLEDGE_PROBLEM_WORDS);

    expect(
      uploadFailure(
        {
          kind: 'unprocessable',
          status: 422,
          message: 'From the catalogue.',
          code: 'knowledge.file.encrypted',
        },
        'ar',
      ),
    ).toBe('From the catalogue.');
  });

  it('keeps the old wording for a code nobody has registered', () => {
    expect(
      uploadFailure(
        {
          kind: 'tooLarge',
          status: 413,
          message: 'server words',
          code: 'knowledge.file.something_new',
        },
        'en',
      ),
    ).toContain('larger than 10 MB');
  });
});

describe('knowledge words', () => {
  it('uses exactly the API states, plus a failed attempt seen on the page', () => {
    expect(knowledgeStateWord('Ready')).toBe('Indexed');
    expect(knowledgeStateWord('NeedsIndex')).toBe('Needs index');
    expect(knowledgeStateWord('Disabled')).toBe('Disabled');
    expect(knowledgeStateWord('Failed')).toBe('Indexing failed');
    expect(knowledgeStateWord('Indexing')).toBe('Indexing now');
    expect(knowledgeStateWord('Stopped')).toBe('Indexing stopped');
    // An unknown state is shown, never hidden.
    expect(knowledgeStateWord('Archived')).toBe('Archived');
  });

  it('gives every state its own chip, so none is told apart by colour alone', () => {
    const tones = ['Ready', 'NeedsIndex', 'Disabled', 'Failed', 'Indexing'].map(knowledgeStateTone);

    expect(tones).toEqual(['success', 'warning', 'neutral', 'danger', 'info']);
    // Stopped shares the triangle with Needs index: both still need an index,
    // and the words differ.
    expect(knowledgeStateTone('Stopped')).toBe('warning');
    expect(knowledgeStateTone('Archived')).toBe('neutral');
  });

  it("draws a document's kind as a glyph: written text, a table, a file", () => {
    expect(knowledgeGlyph(null)).toBe('knowledge');
    expect(knowledgeGlyph('Csv')).toBe('table');
    expect(knowledgeGlyph('Pdf')).toBe('file');
    expect(knowledgeGlyph('Docx')).toBe('file');
    expect(knowledgeGlyph('Text')).toBe('file');
  });

  it('names where the text came from', () => {
    expect(knowledgeTypeWord(null)).toBe('Written');
    expect(knowledgeTypeWord('Pdf')).toBe('PDF');
    expect(knowledgeTypeWord('Docx')).toBe('Word document');
  });

  it('counts characters with the plural form the language needs', () => {
    expect(characterCount('en', 1, '1')).toBe('1 character');
    expect(characterCount('en', 1199, '1,199')).toBe('1,199 characters');
  });
});
