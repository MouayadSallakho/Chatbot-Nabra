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
}

/**
 * The primary navigation.
 *
 * A single exported constant rather than a list written into the sidebar
 * template, because the router needs the same information: routes are generated
 * from this array, which is what makes a dead navigation link structurally
 * impossible rather than merely unlikely.
 *
 * The order groups by how the product is used, not alphabetically: what is
 * happening now (Overview, AI Agents, Conversations), what the agents draw on
 * (Knowledge), how they reach people (Channels, Integrations), then review and
 * configuration (Analytics, Settings).
 */
export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Overview', path: 'overview', icon: 'overview', implemented: true },
  {
    label: 'AI Agents',
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
    // Real from Phase 15: these are the conversations customers have actually
    // had, not a placeholder. Read-only -- replying, assigning and handing off do
    // not exist yet, and the screen says so rather than showing controls that
    // would fail.
    label: 'Conversations',
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
    label: 'Customers',
    path: 'customers',
    icon: 'team',
    implemented: true,
    permissions: ['customers.view'],
  },
  {
    // Real from Phase 17: written documents an agent can actually answer from,
    // not a placeholder. File upload, crawling and connectors are still absent,
    // and the screens say so rather than showing controls that would fail.
    label: 'Knowledge',
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
    label: 'Channels',
    path: 'channels',
    icon: 'channels',
    implemented: true,
    permissions: ['website-chat.view', 'whatsapp.view'],
  },
  { label: 'Integrations', path: 'integrations', icon: 'integrations', implemented: false },
  {
    // Real from Phase 22: counts drawn from the conversations, messages and
    // turns the platform already stores. One permission, because there is
    // nothing here to configure.
    label: 'Analytics',
    path: 'analytics',
    icon: 'analytics',
    implemented: true,
    permissions: ['analytics.view'],
  },
  {
    // Real from Phase 31: the same numbers as Analytics, exported as files and
    // generated on a schedule. Its own permission, because being trusted to read
    // a dashboard is not being trusted to write the workspace's data to a file.
    label: 'Reports',
    path: 'reports',
    icon: 'analytics',
    implemented: true,
    permissions: ['reports.view'],
  },
  {
    // Beside Analytics, behind the same permission. It answers a different
    // question -- what this workspace may hold, rather than what it did -- and
    // there is nothing to configure here, so one read permission is enough.
    label: 'Plan & usage',
    path: 'plan-usage',
    icon: 'analytics',
    implemented: true,
    permissions: ['analytics.view'],
  },
  {
    // Beside Plan & usage rather than inside it. The two are read by different
    // people: one wants to know what the workspace may hold, the other what it
    // costs -- and billing.view is deliberately not analytics.view, because
    // being trusted with conversation counts is not being trusted with money.
    label: 'Billing',
    path: 'billing',
    icon: 'settings',
    implemented: true,
    permissions: ['billing.view'],
  },
  {
    // This replaces the Phase 10 "Settings" placeholder rather than nesting
    // inside it. There are no other settings yet, so a Settings hub would be a
    // page with one link on it -- a dead step that exists only to be completed.
    // When a second settings area arrives, this becomes a child of one.
    label: 'Roles & permissions',
    path: 'roles',
    icon: 'settings',
    implemented: true,
    permissions: ['roles.view'],
  },
  {
    // Beside Roles rather than inside it: a role is a definition of what may be
    // done, and a team is the people who may do it. They are administered by
    // different permissions and, in a real workspace, often by different people.
    label: 'Team',
    path: 'team',
    icon: 'team',
    implemented: true,
    // Only members.view. Inviting and assigning are things done from inside this
    // area, and somebody who can invite but not see the team would arrive at a
    // page that could show them nothing.
    permissions: ['members.view'],
  },
];

/** Where the application lands, and where "not found" sends people back to. */
export const HOME_PATH = 'overview';
