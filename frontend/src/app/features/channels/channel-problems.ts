import type { ProblemCatalogue } from '@app/core/problems/problem-words';

/**
 * The channel screens' refusals (website chat and WhatsApp), by the server's
 * stable `code`.
 *
 * Registered by the channels' routes, so the words arrive with the feature and
 * cost the startup JavaScript nothing. A code that is not here keeps today's
 * generic wording, and the English `detail` is never what an Arabic reader sees.
 */
export const CHANNEL_PROBLEM_WORDS: ProblemCatalogue = {
  'website-chat.agent.required': $localize`:@@problem.websiteChat.agent.required:Choose an agent for this chat link.`,
  'website-chat.agent.unusable': $localize`:@@problem.websiteChat.agent.unusable:This agent cannot be used: it already has a chat link, or it is no longer in this workspace.`,
  'website-chat.origin.invalid': $localize`:@@problem.websiteChat.origin.invalid:One of the websites is not a valid address. Use the scheme and domain only, with no path.`,
  'website-chat.origin.too_many': $localize`:@@problem.websiteChat.origin.too_many:This chat link lists more websites than it may. Remove some and save again.`,
  'website-chat.launcher_position.invalid': $localize`:@@problem.websiteChat.launcher_position.invalid:That corner is not available. Choose one from the list.`,
  'website-chat.theme.invalid': $localize`:@@problem.websiteChat.theme.invalid:That theme is not available. Choose one from the list.`,
  'website-chat.accent_color.required': $localize`:@@problem.websiteChat.accent_color.required:Choose an accent colour.`,
  'website-chat.accent_color.invalid': $localize`:@@problem.websiteChat.accent_color.invalid:Write the colour as # and six digits or letters from a to f, for example #2563eb.`,
  // Worded by the greeting the server names in `field` (Phase 39.1): the
  // default one, or the English or the Arabic one.
  'website-chat.greeting.too_long': ({ field }) =>
    field === 'greetings.en'
      ? $localize`:@@problem.websiteChat.greeting.too_long.en:The English greeting is too long. Shorten it and save again.`
      : field === 'greetings.ar'
        ? $localize`:@@problem.websiteChat.greeting.too_long.ar:The Arabic greeting is too long. Shorten it and save again.`
        : field === 'greeting'
          ? $localize`:@@problem.websiteChat.greeting.too_long.default:The default greeting is too long. Shorten it and save again.`
          : $localize`:@@problem.websiteChat.greeting.too_long:This greeting is too long. Shorten it and save again.`,
  'website-chat.greeting.control_characters': ({ field }) =>
    field === 'greetings.en'
      ? $localize`:@@problem.websiteChat.greeting.control_characters.en:The English greeting has characters that cannot be saved. Paste it as plain text and try again.`
      : field === 'greetings.ar'
        ? $localize`:@@problem.websiteChat.greeting.control_characters.ar:The Arabic greeting has characters that cannot be saved. Paste it as plain text and try again.`
        : field === 'greeting'
          ? $localize`:@@problem.websiteChat.greeting.control_characters.default:The default greeting has characters that cannot be saved. Paste it as plain text and try again.`
          : $localize`:@@problem.websiteChat.greeting.control_characters:This greeting has characters that cannot be saved. Paste it as plain text and try again.`,
  'entitlement.webchatendpoints.exhausted': $localize`:@@problem.entitlement.webchatendpoints.exhausted:This workspace has reached the number of website chat links its plan allows. Nothing was created.`,

  'whatsapp.agent.required': $localize`:@@problem.whatsapp.agent.required:Choose an agent for this number.`,
  'whatsapp.agent.not_in_workspace': $localize`:@@problem.whatsapp.agent.not_in_workspace:This agent is not in this workspace. Reload the page and choose again.`,
  'whatsapp.identifier.invalid': $localize`:@@problem.whatsapp.identifier.invalid:A WhatsApp ID must be 1 to 64 digits, and digits only.`,
  'whatsapp.display_phone_number.too_long': $localize`:@@problem.whatsapp.display_phone_number.too_long:This display number is too long. Shorten it and save again.`,
  'whatsapp.access_token.required': $localize`:@@problem.whatsapp.access_token.required:Enter the access token.`,
  'whatsapp.access_token.too_long': $localize`:@@problem.whatsapp.access_token.too_long:This access token is too long. Check that it was copied correctly.`,
  'whatsapp.connection.in_service': $localize`:@@problem.whatsapp.connection.in_service:Take this number out of service before changing it.`,
  'whatsapp.connection.access_token_missing': $localize`:@@problem.whatsapp.connection.access_token_missing:Add an access token before putting this number into service.`,
  'whatsapp.phone_number.in_use': $localize`:@@problem.whatsapp.phone_number.in_use:This number is already connected, so it cannot be added again.`,
  'entitlement.whatsappconnections.exhausted': $localize`:@@problem.entitlement.whatsappconnections.exhausted:This workspace has reached the number of WhatsApp numbers its plan allows. Nothing was created.`,
};
