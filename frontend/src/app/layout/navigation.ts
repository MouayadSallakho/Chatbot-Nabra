import { IconName } from '@app/shared/icon/icon-paths';
import { PermissionCode } from '@app/core/authorization/permission-codes';

/** One destination in the product's primary navigation. */
export interface NavItem {
  /** What the reader sees, and what a screen reader announces. */
  readonly label: string;

  /** Router path, without a leading slash. */
  readonly path: string;

  readonly icon: IconName;

  /**
   * Whether this area is actually built.
   *
   * Present so the sidebar can be honest. An admin product that lists eight
   * sections and silently does nothing for seven of them wastes the reader's
   * time; marking them lets the placeholder page say plainly what is and is not
   * there yet. It is not a feature flag -- it disappears as each area ships.
   */
  readonly implemented: boolean;

  /**
   * The capabilities that make this area worth showing.
   *
   * The sidebar hides an entry when the signed-in person holds none of them, so
   * nobody is offered a destination that would answer `403` on arrival. An
   * entry with no permissions listed is shown to every member -- which is right
   * for the unbuilt areas, whose placeholder pages say plainly that there is
   * nothing there yet and reveal nothing about the workspace.
   *
   * <strong>Hiding a link is not authorisation.</strong> A reader can type the
   * URL, and the server will still decide. This exists so the navigation does
   * not advertise dead ends.
   */
  readonly permissions?: readonly PermissionCode[];

  /** The sidebar section this destination belongs to. */
  readonly group: NavGroup;
}

/**
 * The sidebar's sections, in the order they appear.
 *
 * Shallow on purpose: five short headings, frequent operational work first and
 * workspace administration last, so the destinations somebody uses every day are
 * never below the ones they open once a quarter.
 */
export const NAV_GROUPS = ['Work', 'AI', 'Channels', 'Insights', 'Workspace'] as const;

export type NavGroup = (typeof NAV_GROUPS)[number];

/**
 * What each section heading says.
 *
 * Kept apart from NAV_GROUPS on purpose: the group names are identifiers (types,
 * element ids, the order of the sidebar) and must not change with the language,
 * while these are words, and must.
 */
export const NAV_GROUP_LABELS: Readonly<Record<NavGroup, string>> = {
  Work: $localize`:@@nav.group.work:Work`,
  AI: $localize`:@@nav.group.ai:AI`,
  Channels: $localize`:@@nav.group.channels:Channels`,
  Insights: $localize`:@@nav.group.insights:Insights`,
  Workspace: $localize`:@@nav.group.workspace:Workspace`,
};

/**
 * The primary navigation.
 *
 * A single exported constant rather than a list written into the sidebar
 * template, because the router needs the same information: routes are generated
 * from this array, which is what makes a dead navigation link structurally
 * impossible rather than merely unlikely.
 *
 * Ordered by group, in NAV_GROUPS order, and within a group by how often it is
 * used: the daily work (Overview, Conversations, Customers), the AI and what it
 * draws on, how it reaches people, what happened, and finally the workspace's
 * own administration. Entries of one group are contiguous, which is what lets
 * the sidebar render them under one heading without re-sorting.
 */
export const NAV_ITEMS: readonly NavItem[] = [
  {
    label: $localize`:@@nav.overview:Overview`,
    path: 'overview',
    icon: 'overview',
    implemented: true,
    group: 'Work',
  },
  {
    // Real from Phase 15: these are the conversations customers have actually
    // had, not a placeholder. Read-only -- replying, assigning and handing off do
    // not exist yet, and the screen says so rather than showing controls that
    // would fail.
    label: $localize`:@@nav.conversations:Conversations`,
    group: 'Work',
    path: 'conversations',
    icon: 'conversations',
    implemented: true,
    permissions: ['conversations.view'],
  },
  {
    // Real from Phase 30: who the workspace talks to, across every channel.
    //
    // Beside Conversations rather than inside it, because the two answer
    // different questions and are read by different people: a directory is an
    // administrative view of the customer base, and a transcript is what
    // somebody said in confidence. customers.view is deliberately not
    // conversations.view.
    label: $localize`:@@nav.customers:Customers`,
    group: 'Work',
    path: 'customers',
    icon: 'customers',
    implemented: true,
    permissions: ['customers.view'],
  },
  {
    label: $localize`:@@nav.aiAgents:AI Agents`,
    group: 'AI',
    path: 'ai-agents',
    icon: 'agents',
    implemented: true,
    // Any of the agent capabilities makes this area worth entering: somebody who
    // can only preview still has a reason to open an agent, even though the list
    // itself will refuse them.
    permissions: [
      'ai-agents.view',
      'ai-agents.create',
      'ai-agents.edit',
      'ai-agents.lifecycle',
      'ai-agents.preview',
    ],
  },
  {
    // Real from Phase 17: written documents an agent can actually answer from,
    // not a placeholder. File upload, crawling and connectors are still absent,
    // and the screens say so rather than showing controls that would fail.
    label: $localize`:@@nav.knowledge:Knowledge`,
    group: 'AI',
    path: 'knowledge',
    icon: 'knowledge',
    implemented: true,
    permissions: ['knowledge.view'],
  },
  {
    // Website chat and WhatsApp are real; Instagram and Messenger are still
    // honestly labelled as not built rather than shown as empty screens, which
    // is why the area is entered through a list rather than straight into one
    // channel.
    //
    // Either channel permission opens it: somebody who administers WhatsApp and
    // not website chat still needs a way in.
    label: $localize`:@@nav.channels:Channels`,
    group: 'Channels',
    path: 'channels',
    icon: 'channels',
    implemented: true,
    permissions: ['website-chat.view', 'whatsapp.view'],
  },
  // Integrations is deliberately absent. It was a placeholder with no screens,
  // and a primary navigation that offers a destination where nothing can be done
  // wastes the reader's time. It returns here, as an implemented entry, when the
  // area is built; until then an unbuilt entry can still be listed with
  // `implemented: false` and gets the generated placeholder route.
  {
    // Real from Phase 22: counts drawn from the conversations, messages and
    // turns the platform already stores. One permission, because there is
    // nothing here to configure.
    label: $localize`:@@nav.analytics:Analytics`,
    group: 'Insights',
    path: 'analytics',
    icon: 'analytics',
    implemented: true,
    permissions: ['analytics.view'],
  },
  {
    // Real from Phase 31: the same numbers as Analytics, exported as files and
    // generated on a schedule. Its own permission, because being trusted to read
    // a dashboard is not being trusted to write the workspace's data to a file.
    label: $localize`:@@nav.reports:Reports`,
    group: 'Insights',
    path: 'reports',
    icon: 'reports',
    implemented: true,
    permissions: ['reports.view'],
  },
  {
    // Beside Roles rather than inside it: a role is a definition of what may be
    // done, and a team is the people who may do it. They are administered by
    // different permissions and, in a real workspace, often by different people.
    label: $localize`:@@nav.team:Team`,
    group: 'Workspace',
    path: 'team',
    icon: 'team',
    implemented: true,
    // Only members.view. Inviting and assigning are things done from inside this
    // area, and somebody who can invite but not see the team would arrive at a
    // page that could show them nothing.
    permissions: ['members.view'],
  },
  {
    // This replaces the Phase 10 "Settings" placeholder rather than nesting
    // inside it. There are no other settings yet, so a Settings hub would be a
    // page with one link on it -- a dead step that exists only to be completed.
    // When a second settings area arrives, this becomes a child of one.
    label: $localize`:@@nav.roles:Roles & permissions`,
    group: 'Workspace',
    path: 'roles',
    icon: 'roles',
    implemented: true,
    permissions: ['roles.view'],
  },
  {
    // Beside Analytics, behind the same permission. It answers a different
    // question -- what this workspace may hold, rather than what it did -- and
    // there is nothing to configure here, so one read permission is enough.
    label: $localize`:@@nav.planUsage:Plan & usage`,
    group: 'Workspace',
    path: 'plan-usage',
    icon: 'usage',
    implemented: true,
    permissions: ['analytics.view'],
  },
  {
    // Beside Plan & usage rather than inside it. The two are read by different
    // people: one wants to know what the workspace may hold, the other what it
    // costs -- and billing.view is deliberately not analytics.view, because
    // being trusted with conversation counts is not being trusted with money.
    label: $localize`:@@nav.billing:Billing`,
    group: 'Workspace',
    path: 'billing',
    icon: 'billing',
    implemented: true,
    permissions: ['billing.view'],
  },
];

/** Where the application lands, and where "not found" sends people back to. */
export const HOME_PATH = 'overview';

/**
 * The navigation entries a member has a reason to see.
 *
 * One rule for every place that offers destinations -- the sidebar and the
 * command palette -- so the two can never disagree about what somebody may
 * open. Not authorisation: the server decides regardless (see `permissions`).
 */
export function visibleNavItems(
  hasAny: (...codes: readonly PermissionCode[]) => boolean,
): readonly NavItem[] {
  return NAV_ITEMS.filter((item) => item.permissions === undefined || hasAny(...item.permissions));
}
