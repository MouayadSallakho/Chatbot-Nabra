/**
 * Bounds the editor shows a reader, mirrored from the Domain.
 *
 * <strong>These are platform limits on what an agent may contain, not model
 * limits.</strong> Twenty thousand characters is what this platform stores in an
 * agent's instructions -- it is not an OpenAI token budget, not a context
 * window, and not related to what any provider will accept. Presenting it as one
 * would be wrong today and would become a lie the moment a model changed.
 *
 * <strong>The Domain remains the authority.</strong> These exist so a counter
 * can tick down while somebody types, and so a form can refuse an obviously
 * over-long value without a round trip. Every value is still validated on the
 * server, and a rejected one is reported in the server's own words rather than
 * re-derived here.
 */
export const AGENT_LIMITS = {
  /** Maximum characters in an agent's display name. Mirrors `AiAgentName`. */
  nameMax: 120,

  /** Maximum characters of instructions. Mirrors `AgentInstructions`. */
  instructionsMax: 20_000,

  /** Maximum characters in one preview message. Mirrors `AiTurn`. */
  previewMessageMax: 10_000,
} as const;

/**
 * Navigation-state key carrying a just-created agent to the editor.
 *
 * Lets the editor open on the representation the create call already returned --
 * body and entity tag together -- instead of immediately re-reading something it
 * was just handed. Purely an optimisation: the editor reads the agent itself
 * whenever the handoff is absent, which is every case except this one.
 */
export const CREATED_AGENT_STATE = 'omniai.createdAgent';
