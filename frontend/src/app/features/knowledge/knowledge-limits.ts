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
