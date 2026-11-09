import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The refusals of creating a workspace, by the server's stable `code`.
 *
 * Registered when the page opens, because its route is declared in the eagerly
 * loaded route table and a route provider would put these words in the startup
 * JavaScript.
 */
export const WORKSPACE_PROBLEM_WORDS: ProblemCatalogue = {
  'workspace.name.invalid': $localize`:@@problem.workspace.name.invalid:Enter a workspace name of up to 200 characters, on one line.`,
  'workspace.slug.taken': $localize`:@@problem.workspace.slug.taken:That workspace slug is already taken. Choose a different one.`,
  'workspace.slug.invalid': $localize`:@@problem.workspace.slug.invalid:Use lowercase letters, numbers and single hyphens for the workspace slug, between 3 and 63 characters.`,
};
