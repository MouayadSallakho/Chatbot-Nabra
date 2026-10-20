import type { AppLanguage } from '@app/core/i18n/language';
import { formatNumber } from '@app/core/i18n/locale-format';
import { pluralMessage } from '@app/core/i18n/plural';
import type { IconName } from '@app/shared/icon/icon-paths';
import type { PermissionDefinition } from '@app/core/authorization/permission-codes';

/**
 * Permissions' words, written once (Phase 36.10).
 *
 * The catalogue arrives from the server in English: its codes are the
 * contract, its labels and descriptions are prose. The words for the codes
 * this build knows are written here in both languages, by code; a code a later
 * server adds keeps the server's own label and description rather than being
 * hidden or guessed. Areas (the server's `group`) work the same way, by the
 * server's English group name.
 */

interface PermissionText {
  readonly label: string;
  readonly description: string;
}

const PERMISSIONS: Readonly<Record<string, PermissionText>> = {
  'workspace.view': {
    label: $localize`:@@permission.workspace.view:View workspace`,
    description: $localize`:@@permission.workspace.view.description:See the workspace's name and status.`,
  },
  'ai-agents.view': {
    label: $localize`:@@permission.aiAgents.view:View AI agents`,
    description: $localize`:@@permission.aiAgents.view.description:See the workspace's AI agents and read their instructions.`,
  },
  'ai-agents.create': {
    label: $localize`:@@permission.aiAgents.create:Create AI agents`,
    description: $localize`:@@permission.aiAgents.create.description:Add a new AI agent to the workspace.`,
  },
  'ai-agents.edit': {
    label: $localize`:@@permission.aiAgents.edit:Edit AI agents`,
    description: $localize`:@@permission.aiAgents.edit.description:Change an AI agent's name and instructions.`,
  },
  'ai-agents.lifecycle': {
    label: $localize`:@@permission.aiAgents.lifecycle:Activate and disable AI agents`,
    description: $localize`:@@permission.aiAgents.lifecycle.description:Decide whether an AI agent is live or taken out of service.`,
  },
  'ai-agents.preview': {
    label: $localize`:@@permission.aiAgents.preview:Preview AI agents`,
    description: $localize`:@@permission.aiAgents.preview.description:Try an AI agent out with a test message. This is the only capability that sends a request to the AI provider.`,
  },
  'roles.view': {
    label: $localize`:@@permission.roles.view:View roles`,
    description: $localize`:@@permission.roles.view.description:See the workspace's roles and which permissions each one grants.`,
  },
  'roles.manage': {
    label: $localize`:@@permission.roles.manage:Manage roles`,
    description: $localize`:@@permission.roles.manage.description:Create custom roles and change their names and permissions. It does not give roles to members.`,
  },
  'members.view': {
    label: $localize`:@@permission.members.view:View team members`,
    description: $localize`:@@permission.members.view.description:See who belongs to this workspace, which roles they hold, and which invitations are still open.`,
  },
  'members.invite': {
    label: $localize`:@@permission.members.invite:Invite team members`,
    description: $localize`:@@permission.members.invite.description:Create and revoke invitations. On its own it invites somebody who can do nothing until they are given a role.`,
  },
  'members.roles.manage': {
    label: $localize`:@@permission.members.rolesManage:Assign roles to team members`,
    description: $localize`:@@permission.members.rolesManage.description:Decide which roles a member holds. You can only give away, and only take away, what your own roles let you do.`,
  },
  'conversations.view': {
    label: $localize`:@@permission.conversations.view:View conversations`,
    description: $localize`:@@permission.conversations.view.description:Read the conversations customers have had with this workspace, including everything they wrote.`,
  },
  'conversations.reply': {
    label: $localize`:@@permission.conversations.reply:Reply to conversations`,
    description: $localize`:@@permission.conversations.reply.description:Write to customers as this workspace. Members who hold it are also the members a conversation can be assigned to.`,
  },
  'conversations.handoff': {
    label: $localize`:@@permission.conversations.handoff:Take conversations over from the AI`,
    description: $localize`:@@permission.conversations.handoff.description:Stop the AI answering a conversation so a person can, and hand it back again afterwards.`,
  },
  'conversations.assign': {
    label: $localize`:@@permission.conversations.assign:Assign conversations`,
    description: $localize`:@@permission.conversations.assign.description:Decide which team member a conversation is routed to. It gives that member no permission they did not already have.`,
  },
  'customers.view': {
    label: $localize`:@@permission.customers.view:View customers`,
    description: $localize`:@@permission.customers.view.description:See who this workspace talks to, which channels they use and how much they have talked. It does not grant access to what they said.`,
  },
  'customers.manage': {
    label: $localize`:@@permission.customers.manage:Manage customers`,
    description: $localize`:@@permission.customers.manage.description:Name a customer, and combine two records that turn out to be the same person. Combining cannot be undone.`,
  },
  'website-chat.view': {
    label: $localize`:@@permission.websiteChat.view:View website chat`,
    description: $localize`:@@permission.websiteChat.view.description:See how this workspace's website chat is set up and which agent answers on it.`,
  },
  'website-chat.manage': {
    label: $localize`:@@permission.websiteChat.manage:Manage website chat`,
    description: $localize`:@@permission.websiteChat.manage.description:Create website chat links and decide whether the public can use them.`,
  },
  'whatsapp.view': {
    label: $localize`:@@permission.whatsapp.view:View WhatsApp`,
    description: $localize`:@@permission.whatsapp.view.description:See how the workspace's WhatsApp channel is set up and whether it is in service. It never reveals the access token.`,
  },
  'whatsapp.manage': {
    label: $localize`:@@permission.whatsapp.manage:Manage WhatsApp`,
    description: $localize`:@@permission.whatsapp.manage.description:Connect a WhatsApp number to an AI agent, replace its access token, and take the channel in and out of service.`,
  },
  'knowledge.view': {
    label: $localize`:@@permission.knowledge.view:View knowledge`,
    description: $localize`:@@permission.knowledge.view.description:Read the workspace's knowledge documents, see whether each one is indexed, and test what an agent would retrieve for a question.`,
  },
  'knowledge.manage': {
    label: $localize`:@@permission.knowledge.manage:Manage knowledge`,
    description: $localize`:@@permission.knowledge.manage.description:Write and edit knowledge documents, index them, switch them on and off, and choose which AI agents may draw on them. Indexing sends the document's text to the AI provider and costs money.`,
  },
  'analytics.view': {
    label: $localize`:@@permission.analytics.view:View analytics`,
    description: $localize`:@@permission.analytics.view.description:See how many conversations the workspace handled, how its channels and AI agents are being used, and how much activity happened each day. Counts only, never the text of a conversation.`,
  },
  'reports.view': {
    label: $localize`:@@permission.reports.view:View reports`,
    description: $localize`:@@permission.reports.view.description:See which reports exist and download generated ones. Each download also needs the permission that report's data needs.`,
  },
  'reports.generate': {
    label: $localize`:@@permission.reports.generate:Generate reports`,
    description: $localize`:@@permission.reports.generate.description:Export the workspace's numbers as CSV, Excel or PDF files. Counts and safe labels only, never the text of a conversation.`,
  },
  'reports.schedule': {
    label: $localize`:@@permission.reports.schedule:Schedule reports`,
    description: $localize`:@@permission.reports.schedule.description:Create weekly and monthly reports that are generated automatically under your own permissions.`,
  },
  'billing.view': {
    label: $localize`:@@permission.billing.view:View billing`,
    description: $localize`:@@permission.billing.view.description:See the workspace's subscription, what it is paying for and what else it could buy. Never a card number: payment details live with the payment provider and never reach Nabra.`,
  },
  'billing.manage': {
    label: $localize`:@@permission.billing.manage:Manage billing`,
    description: $localize`:@@permission.billing.manage.description:Start a subscription and open the payment provider's own billing portal, where payment methods, invoices and cancellation are handled. This is authority to spend the workspace's money.`,
  },
};

/** The areas, by the server's English group name. */
const AREAS: Readonly<Record<string, string>> = {
  Workspace: $localize`:@@permission.area.workspace:Workspace`,
  'AI agents': $localize`:@@permission.area.aiAgents:AI agents`,
  'Roles and permissions': $localize`:@@permission.area.roles:Roles and permissions`,
  'Team members': $localize`:@@permission.area.members:Team members`,
  Conversations: $localize`:@@permission.area.conversations:Conversations`,
  Customers: $localize`:@@permission.area.customers:Customers`,
  Channels: $localize`:@@permission.area.channels:Channels`,
  Knowledge: $localize`:@@permission.area.knowledge:Knowledge`,
  Analytics: $localize`:@@permission.area.analytics:Analytics`,
  Reports: $localize`:@@permission.area.reports:Reports`,
  Billing: $localize`:@@permission.area.billing:Billing`,
};

/** A permission's label in the reader's language; an unknown code keeps the server's. */
export function permissionLabel(permission: PermissionDefinition): string {
  return PERMISSIONS[permission.code]?.label ?? permission.label;
}

/** What a permission allows, in the reader's language; an unknown code keeps the server's. */
export function permissionDescription(permission: PermissionDefinition): string {
  return PERMISSIONS[permission.code]?.description ?? permission.description;
}

/** An area's name in the reader's language; an unknown area keeps the server's. */
export function areaName(group: string): string {
  return AREAS[group] ?? group;
}

/**
 * What holding a permission can lead to, beyond the sentence describing it.
 *
 * Each hint restates something the server's own description already says
 * (it reads what customers wrote, it costs money, it cannot be undone), so a
 * reader scanning a long list sees the consequential ones without reading
 * every line. Nothing here is a rule the server enforces; it is a reading aid.
 */
export type PermissionImpact =
  'customerWords' | 'customers' | 'cost' | 'money' | 'irreversible' | 'access';

const IMPACTS: Readonly<Record<string, readonly PermissionImpact[]>> = {
  'ai-agents.edit': ['customers'],
  'ai-agents.lifecycle': ['customers'],
  'ai-agents.preview': ['cost'],
  'roles.manage': ['access'],
  'members.invite': ['access'],
  'members.roles.manage': ['access'],
  'conversations.view': ['customerWords'],
  'conversations.reply': ['customers'],
  'conversations.handoff': ['customers'],
  'customers.manage': ['irreversible'],
  'website-chat.manage': ['customers'],
  'whatsapp.manage': ['customers'],
  'knowledge.manage': ['cost'],
  'billing.manage': ['money'],
};

export interface ImpactHint {
  readonly kind: PermissionImpact;
  readonly icon: IconName;
  readonly text: string;
}

const IMPACT_HINTS: Readonly<Record<PermissionImpact, ImpactHint>> = {
  customerWords: {
    kind: 'customerWords',
    icon: 'conversations',
    text: $localize`:@@permission.impact.customerWords:Reads what customers wrote`,
  },
  customers: {
    kind: 'customers',
    icon: 'customers',
    text: $localize`:@@permission.impact.customers:Customers see the result`,
  },
  cost: {
    kind: 'cost',
    icon: 'usage',
    text: $localize`:@@permission.impact.cost:Uses the AI provider, which costs money`,
  },
  money: {
    kind: 'money',
    icon: 'billing',
    text: $localize`:@@permission.impact.money:Spends the workspace's money`,
  },
  irreversible: {
    kind: 'irreversible',
    icon: 'alert',
    text: $localize`:@@permission.impact.irreversible:Includes a step that cannot be undone`,
  },
  access: {
    kind: 'access',
    icon: 'team',
    text: $localize`:@@permission.impact.access:Changes who has access`,
  },
};

/** The impact hints for a permission, most of which have none. */
export function permissionImpacts(code: string): readonly ImpactHint[] {
  return (IMPACTS[code] ?? []).map((kind) => IMPACT_HINTS[kind]);
}

/**
 * Every impact the ticked permissions add up to, in a fixed order, once each:
 * the role-level answer to "what could somebody holding this do?".
 */
export function impactSummary(codes: Iterable<string>): readonly ImpactHint[] {
  const kinds = new Set<PermissionImpact>();

  for (const code of codes) {
    for (const kind of IMPACTS[code] ?? []) {
      kinds.add(kind);
    }
  }

  return IMPACT_ORDER.filter((kind) => kinds.has(kind)).map((kind) => IMPACT_HINTS[kind]);
}

const IMPACT_ORDER: readonly PermissionImpact[] = [
  'money',
  'access',
  'irreversible',
  'customerWords',
  'customers',
  'cost',
];

/** "Grants 10 of 29 permissions", with every Arabic plural form. */
export function grantSummary(count: number, total: number, language: AppLanguage): string {
  const n = formatNumber(language, count);
  const all = formatNumber(language, total);

  return pluralMessage(language, count, {
    zero: $localize`:@@roles.summary.zero:Grants none of the ${all}:total: permissions`,
    one: $localize`:@@roles.summary.one:Grants ${n}:count: of ${all}:total: permissions`,
    two: $localize`:@@roles.summary.two:Grants ${n}:count: of ${all}:total: permissions`,
    few: $localize`:@@roles.summary.few:Grants ${n}:count: of ${all}:total: permissions`,
    many: $localize`:@@roles.summary.many:Grants ${n}:count: of ${all}:total: permissions`,
    other: $localize`:@@roles.summary.other:Grants ${n}:count: of ${all}:total: permissions`,
  });
}

/** "10 permissions", for a role in the list or a role offered to a member. */
export function permissionCount(count: number, language: AppLanguage): string {
  const n = formatNumber(language, count);

  return pluralMessage(language, count, {
    zero: $localize`:@@roles.count.zero:No permissions`,
    one: $localize`:@@roles.count.one:${n}:count: permission`,
    two: $localize`:@@roles.count.two:${n}:count: permissions`,
    few: $localize`:@@roles.count.few:${n}:count: permissions`,
    many: $localize`:@@roles.count.many:${n}:count: permissions`,
    other: $localize`:@@roles.count.other:${n}:count: permissions`,
  });
}

/**
 * A role's name as the reader sees it. Custom role names are the workspace's
 * own words and are never translated; the one system role the platform
 * defines, Owner, is a product word and is said in the reader's language.
 * Another system role a later server adds keeps its name as sent.
 */
export function roleDisplayName(name: string, isSystem: boolean): string {
  return isSystem && name === 'Owner' ? $localize`:@@roles.system.owner:Owner` : name;
}
