/**
 * The bounds the knowledge screens enforce in the browser.
 *
 * <strong>They mirror the backend's own, and they are not the enforcement.</strong>
 * The server refuses anything past them regardless; these exist so somebody
 * typing gets told before they press Save rather than after, and so a character
 * counter can say how much room is left. A mismatch here would only ever produce
 * a confusing rejection, never an unsafe write.
 */
export const KNOWLEDGE_LIMITS = {
  /** Matches `KnowledgeTitle.MaxLength`. */
  titleMax: 200,

  /** Matches `KnowledgeContent.MaxLength`. */
  contentMax: 100_000,

  /** Matches `SearchAgentKnowledgeHandler.MaxQuestionLength`. */
  questionMax: 2_000,
} as const;

/**
 * Router state key for a document handed straight from the create page to the
 * editor.
 *
 * The same device the agent editor uses: the create response already carries the
 * document and its tag, so navigating to the editor need not read it back.
 */
export const CREATED_DOCUMENT_STATE = 'createdKnowledgeDocument';

/**
 * What the upload form accepts, mirroring the backend's own policy.
 *
 * <strong>Again, not the enforcement.</strong> `accept` on a file input is a
 * convenience for the file picker and nothing more -- a browser will happily
 * submit anything past it, and the backend decides what a file really is by
 * reading its signature rather than trusting its name. These values exist so the
 * picker shows the right files and the page can state the limit in a sentence.
 */
export const KNOWLEDGE_FILE_LIMITS = {
  /** Matches `KnowledgeFilePolicy.MaxFileBytes`. */
  maxBytes: 10 * 1024 * 1024,

  /** The same number in the unit the page says out loud. */
  maxMegabytes: 10,

  /**
   * The file picker's filter.
   *
   * Both extensions and media types, because browsers differ in which they
   * honour, and a picker that greys out a valid PDF is worse than one that lets
   * an invalid file through to a clear rejection.
   */
  accept:
    '.pdf,.docx,.txt,.csv,application/pdf,' +
    'application/vnd.openxmlformats-officedocument.wordprocessingml.document,text/plain,text/csv',
} as const;

/** The human names of the formats, in the order the page lists them. */
export const KNOWLEDGE_FILE_FORMATS = ['PDF', 'Word (.docx)', 'Plain text', 'CSV'] as const;

/** Router state key for a document handed from the upload page to the editor. */
export const UPLOADED_DOCUMENT_STATE = 'uploadedKnowledgeDocument';
