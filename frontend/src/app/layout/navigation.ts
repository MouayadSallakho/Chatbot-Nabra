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
  { label: 'Conversations', path: 'conversations', icon: 'conversations', implemented: false },
  { label: 'Knowledge', path: 'knowledge', icon: 'knowledge', implemented: false },
  { label: 'Channels', path: 'channels', icon: 'channels', implemented: false },
  { label: 'Integrations', path: 'integrations', icon: 'integrations', implemented: false },
  { label: 'Analytics', path: 'analytics', icon: 'analytics', implemented: false },
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
];

/** Where the application lands, and where "not found" sends people back to. */
export const HOME_PATH = 'overview';
