import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The role screens' refusals, by the server's stable `code`.
 *
 * Registered by the roles routes, so the words arrive with the feature and cost
 * the startup JavaScript nothing. A code that is not here keeps today's generic
 * wording, and the English `detail` is never what an Arabic reader sees.
 */
export const ROLE_PROBLEM_WORDS: ProblemCatalogue = {
  'roles.name.required': $localize`:@@problem.roles.name.required:Enter a name for the role.`,
  'roles.name.too_long': $localize`:@@problem.roles.name.too_long:This role name is too long. Shorten it and save again.`,
  'roles.name.control_characters': $localize`:@@problem.roles.name.control_characters:This role name has characters that cannot be saved. Type it on one line as plain text.`,
  'roles.name.taken': $localize`:@@problem.roles.name.taken:Another role in this workspace already has this name. Choose a different one.`,
  'roles.permission.invalid': $localize`:@@problem.roles.permission.invalid:One of the permissions could not be read. Reload the page and choose again.`,
  'roles.permission.unknown': $localize`:@@problem.roles.permission.unknown:One of the permissions is not available on this installation. Reload the page and choose again.`,
  'roles.system.protected': $localize`:@@problem.roles.system.protected:The Owner role is protected: it cannot be renamed and always has every permission.`,
  'roles.id.invalid': $localize`:@@problem.roles.id.invalid:This address does not lead to a role. Go back to the roles and choose one.`,
};
