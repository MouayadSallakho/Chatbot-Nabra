import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { BILLING_PROBLEM_WORDS } from '@app/features/billing/billing-problems';
import { SEAT_PROBLEM_WORDS } from '@app/features/invitations/seat-problems';
import { ROLE_PROBLEM_WORDS } from '@app/features/roles/role-problems';
import { TEAM_PROBLEM_WORDS } from './team-problems';

/**
 * The team, role, invitation, email-address and billing refusals (Phase 38.4,
 * D7): every code `problem-codes.md` lists for these screens, each in words of
 * its own. The Arabic is proved in `arabic-render.spec.ts`.
 */
describe('admin refusals by code (Phase 38.4)', () => {
  const refusal = (code: string, status = 409) =>
    new HttpErrorResponse({
      status,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code },
    });

  const catalogues = {
    TEAM_PROBLEM_WORDS,
    ROLE_PROBLEM_WORDS,
    BILLING_PROBLEM_WORDS,
    SEAT_PROBLEM_WORDS,
  };

  beforeAll(() => Object.values(catalogues).forEach(registerProblemWords));

  it.each(Object.entries(catalogues))('%s never shows the server sentence', (_name, catalogue) => {
    for (const code of Object.keys(catalogue)) {
      const message = toApiError(refusal(code)).message;

      expect(message.length).toBeGreaterThan(0);
      expect(message).not.toContain('The English sentence');
    }
  });

  it('covers every role code', () => {
    expect(Object.keys(ROLE_PROBLEM_WORDS).sort()).toEqual(
      [
        'roles.name.required',
        'roles.name.too_long',
        'roles.name.control_characters',
        'roles.name.taken',
        'roles.permission.invalid',
        'roles.permission.unknown',
        'roles.system.protected',
        'roles.id.invalid',
      ].sort(),
    );
  });

  it('covers every invitation, team, email-address and seat code the team screens meet', () => {
    expect(Object.keys(TEAM_PROBLEM_WORDS).sort()).toEqual(
      [
        'invitations.status.invalid',
        'invitations.role.unassignable',
        'invitations.email.already_member',
        'invitations.email.already_invited',
        'invitations.revoke.closed',
        'invitations.id.invalid',
        'team.member.self',
        'team.member.owner_protected',
        'team.member.protected',
        'team.role.unassignable',
        'email.required',
        'email.too_long',
        'email.invalid_characters',
        'email.invalid',
        'entitlement.members.exhausted',
      ].sort(),
    );
  });

  it('covers every billing code', () => {
    expect(Object.keys(BILLING_PROBLEM_WORDS).sort()).toEqual(
      [
        'billing.offer_id.invalid',
        'billing.checkout.unknownoffer',
        'billing.checkout.offernotavailable',
        'billing.checkout.alreadysubscribed',
        'billing.checkout.offernotpurchasable',
      ].sort(),
    );
  });

  it.each([
    ['roles.name.taken', 'already has this name'],
    ['roles.system.protected', 'The Owner role is protected'],
    ['invitations.email.already_member', 'already belongs to a member'],
    ['invitations.revoke.closed', 'nothing to revoke'],
    ['team.member.self', 'your own access'],
    ['email.invalid_characters', 'spaces or hidden characters'],
    ['entitlement.members.exhausted', 'number of members its plan allows'],
    ['billing.checkout.alreadysubscribed', 'already has a subscription'],
  ])('words %s by its code', (code, words) => {
    expect(toApiError(refusal(code)).message).toContain(words);
  });

  it('says that nothing was charged for every billing refusal', () => {
    for (const code of Object.keys(BILLING_PROBLEM_WORDS)) {
      expect(toApiError(refusal(code)).message).toContain('Nothing was charged.');
    }
  });
});
