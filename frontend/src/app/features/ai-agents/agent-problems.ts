import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The AI agent screens' refusals, by the server's stable `code`.
 *
 * Registered by the agents' routes, so the words arrive with the feature and cost
 * the startup JavaScript nothing. A code that is not here keeps today's generic
 * wording, and the English `detail` is never what an Arabic reader sees.
 */
export const AGENT_PROBLEM_WORDS: ProblemCatalogue = {
  'ai-agents.name.required': $localize`:@@problem.agents.name.required:Give this agent a name.`,
  'ai-agents.name.too_long': $localize`:@@problem.agents.name.too_long:This name is too long. Shorten it and save again.`,
  'ai-agents.name.control_characters': $localize`:@@problem.agents.name.control_characters:A name has to be plain text on one line.`,
  'ai-agents.instructions.required': $localize`:@@problem.agents.instructions.required:Write instructions for this agent.`,
  'ai-agents.instructions.too_long': $localize`:@@problem.agents.instructions.too_long:These instructions are too long. Shorten them and save again.`,
  'ai-agents.instructions.control_characters': $localize`:@@problem.agents.instructions.control_characters:These instructions have characters that cannot be saved. Paste them as plain text and try again.`,
  'ai-agents.disable.draft': $localize`:@@problem.agents.disable.draft:A draft was never in service, so there is nothing to switch off.`,
  'ai.message.invalid': $localize`:@@problem.ai.message.invalid:This message cannot be sent. Check that it is not blank or too long.`,
  'entitlement.aiagents.exhausted': $localize`:@@problem.entitlement.aiagents.exhausted:This workspace has reached the number of AI agents its plan allows. Nothing was saved.`,
};
