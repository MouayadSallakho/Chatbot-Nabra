import { IconName } from '@app/shared/icon/icon-paths';

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
  { label: 'AI Agents', path: 'ai-agents', icon: 'agents', implemented: false },
  { label: 'Conversations', path: 'conversations', icon: 'conversations', implemented: false },
  { label: 'Knowledge', path: 'knowledge', icon: 'knowledge', implemented: false },
  { label: 'Channels', path: 'channels', icon: 'channels', implemented: false },
  { label: 'Integrations', path: 'integrations', icon: 'integrations', implemented: false },
  { label: 'Analytics', path: 'analytics', icon: 'analytics', implemented: false },
  { label: 'Settings', path: 'settings', icon: 'settings', implemented: false },
];

/** Where the application lands, and where "not found" sends people back to. */
export const HOME_PATH = 'overview';
