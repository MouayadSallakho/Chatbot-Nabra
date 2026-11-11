import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The member-seat refusal, which two screens can meet: creating an invitation
 * and accepting one. One sentence that is true on both, in its own file so the
 * acceptance page carries this and nothing else of the team's words.
 */
export const SEAT_PROBLEM_WORDS: ProblemCatalogue = {
  'entitlement.members.exhausted': $localize`:@@problem.entitlement.members.exhausted:This workspace has reached the number of members its plan allows, so nobody else can join it now. Nothing was changed.`,
};
