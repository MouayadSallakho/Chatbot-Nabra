/**
 * The permission codes this application knows how to react to.
 *
 * <strong>A union type, so a typo is a compile error rather than a silently
 * hidden button.</strong> Writing `has('ai-agents.edt')` in a template would
 * otherwise be perfectly valid TypeScript that always answers false — and the
 * symptom would be an editor that is read-only for everybody, with nothing in
 * the console to say why.
 *
 * <strong>These are UX inputs, not a security boundary.</strong> The server
 * decides. This union exists so the client asks coherent questions, not so it
 * can enforce answers.
 *
 * It deliberately lists only the codes this client actually branches on. The
 * catalogue endpoint returns whatever the platform defines, including codes a
 * future release adds before this file knows about them; those render perfectly
 * well in the role editor as data. This type is about the handful the admin's own
 * screens consult.
 */
export type PermissionCode =
  | 'workspace.view'
  | 'ai-agents.view'
  | 'ai-agents.create'
  | 'ai-agents.edit'
  | 'ai-agents.lifecycle'
  | 'ai-agents.preview'
  | 'roles.view'
  | 'roles.manage'
  | 'members.view'
  | 'members.invite'
  | 'members.roles.manage'
  | 'conversations.view'
  | 'conversations.reply'
  | 'conversations.handoff'
  | 'conversations.assign'
  | 'website-chat.view'
  | 'website-chat.manage'
  | 'whatsapp.view'
  | 'whatsapp.manage'
  | 'knowledge.view'
  | 'knowledge.manage'
  | 'analytics.view';

/**
 * One capability, as the catalogue endpoint publishes it.
 *
 * `code` is the contract; the rest is what a person needs in order to decide
 * whether to grant it. The role editor renders all four and sends back only the
 * code.
 */
export interface PermissionDefinition {
  readonly code: string;
  readonly group: string;
  readonly label: string;
  readonly description: string;
}
