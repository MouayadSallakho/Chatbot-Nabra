import type { ProblemCatalogue } from '@app/core/problems/problem-words';
import { SEAT_PROBLEM_WORDS } from '@app/features/invitations/seat-problems';

/**
 * The team screens' refusals (members, invitations and the addresses they are
 * sent to), by the server's stable `code`.
 *
 * Registered by the team routes, so the words arrive with the feature and cost
 * the startup JavaScript nothing. A code that is not here keeps today's generic
 * wording, and the English `detail` is never what an Arabic reader sees.
 */
export const TEAM_PROBLEM_WORDS: ProblemCatalogue = {
  ...SEAT_PROBLEM_WORDS,

  'invitations.status.invalid': $localize`:@@problem.invitations.status.invalid:That list of invitations does not exist. Choose another one.`,
  'invitations.role.unassignable': $localize`:@@problem.invitations.role.unassignable:One of the chosen roles is no longer in this workspace. Reload the page and choose again.`,
  'invitations.email.already_member': $localize`:@@problem.invitations.email.already_member:This address already belongs to a member. Change their roles from the team page instead.`,
  'invitations.email.already_invited': $localize`:@@problem.invitations.email.already_invited:An invitation for this address is still waiting. Revoke it first to send a new one.`,
  'invitations.revoke.closed': $localize`:@@problem.invitations.revoke.closed:This invitation was already accepted, revoked or expired, so there is nothing to revoke.`,
  'invitations.id.invalid': $localize`:@@problem.invitations.id.invalid:This invitation could not be found. Reload the page.`,

  'team.member.self': $localize`:@@problem.team.member.self:You cannot change your own access here. Ask another administrator to do it.`,
  'team.member.owner_protected': $localize`:@@problem.team.member.owner_protected:The owner's access is not changed here, and the Owner role is never given from the team page.`,
  'team.member.protected': $localize`:@@problem.team.member.protected:This member's access is protected and cannot be changed here.`,
  'team.role.unassignable': $localize`:@@problem.team.role.unassignable:One of the chosen roles is no longer in this workspace. Reload the page and choose again.`,

  'email.required': $localize`:@@team.invite.email.required:Enter the email address to invite.`,
  'email.too_long': $localize`:@@problem.email.too_long:This email address is too long. Check it and try again.`,
  'email.invalid_characters': $localize`:@@problem.email.invalid_characters:Remove the spaces or hidden characters from the email address.`,
  'email.invalid': $localize`:@@problem.auth.email.invalid:That email address cannot be used. Check it and try again.`,
};
