import { HttpErrorResponse } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { AGENT_PROBLEM_WORDS } from '@app/features/ai-agents/agent-problems';
import { WORKSPACE_PROBLEM_WORDS } from '@app/features/workspace-setup/workspace-problems';
import { CHAT_PROBLEM_WORDS } from '@app/shared/chat/chat-problems';
import { CHANNEL_PROBLEM_WORDS } from './channel-problems';

/** The catalogues with no bound to say: each code in words of its own. */
describe('area refusals by code (Phase 38.3)', () => {
  const refusal = (code: string) =>
    new HttpErrorResponse({
      status: 400,
      error: { title: 'Refused', detail: 'The English sentence from the server.', code },
    });

  const catalogues = {
    AGENT_PROBLEM_WORDS,
    CHANNEL_PROBLEM_WORDS,
    CHAT_PROBLEM_WORDS,
    WORKSPACE_PROBLEM_WORDS,
  };

  beforeAll(() => Object.values(catalogues).forEach(registerProblemWords));

  it.each(Object.entries(catalogues))('%s never shows the server sentence', (_name, catalogue) => {
    for (const code of Object.keys(catalogue)) {
      const message = toApiError(refusal(code)).message;

      expect(message.length).toBeGreaterThan(0);
      expect(message).not.toContain('The English sentence');
    }
  });

  it('covers the website chat codes a screen can meet', () => {
    expect(Object.keys(CHANNEL_PROBLEM_WORDS)).toEqual(
      expect.arrayContaining([
        'website-chat.agent.required',
        'website-chat.agent.unusable',
        'website-chat.origin.invalid',
        'website-chat.origin.too_many',
        'website-chat.launcher_position.invalid',
        'website-chat.theme.invalid',
        'website-chat.accent_color.required',
        'website-chat.accent_color.invalid',
        'website-chat.greeting.too_long',
        'website-chat.greeting.control_characters',
        'entitlement.webchatendpoints.exhausted',
      ]),
    );
  });

  it('covers the WhatsApp codes the admin screens can meet', () => {
    expect(Object.keys(CHANNEL_PROBLEM_WORDS)).toEqual(
      expect.arrayContaining([
        'whatsapp.agent.required',
        'whatsapp.agent.not_in_workspace',
        'whatsapp.identifier.invalid',
        'whatsapp.display_phone_number.too_long',
        'whatsapp.access_token.required',
        'whatsapp.access_token.too_long',
        'whatsapp.connection.in_service',
        'whatsapp.connection.access_token_missing',
        'whatsapp.phone_number.in_use',
        'entitlement.whatsappconnections.exhausted',
      ]),
    );
  });

  it.each([
    ['ai-agents.disable.draft', 'A draft was never in service'],
    ['website-chat.origin.invalid', 'not a valid address'],
    ['website-chat.origin.too_many', 'more websites than it may'],
    ['whatsapp.connection.in_service', 'out of service before changing it'],
    ['workspace.slug.taken', 'already taken'],
    ['chat.message.in_progress', 'still being answered'],
  ])('words %s by its code', (code, words) => {
    expect(toApiError(refusal(code)).message).toContain(words);
  });
});
