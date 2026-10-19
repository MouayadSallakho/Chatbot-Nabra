import { signal } from '@angular/core';
import { Observable, of } from 'rxjs';
import { PermissionDefinition } from '@app/core/authorization/permission-codes';
import { MediaQueryFactory } from '@app/core/media/media';

/**
 * A media query the test controls.
 *
 * jsdom does not implement `window.matchMedia` at all, so there is nothing to
 * stub over -- and even in a real browser a test cannot change the operating
 * system's colour scheme or the window's width. Driving both through a double is
 * the only way to assert on theme resolution and on the responsive shell at all.
 */
class FakeMediaQueryList implements MediaQueryList {
  onchange: ((this: MediaQueryList, ev: MediaQueryListEvent) => unknown) | null = null;

  private readonly listeners = new Set<EventListenerOrEventListenerObject>();

  constructor(
    readonly media: string,
    private currentMatches: boolean,
  ) {}

  get matches(): boolean {
    return this.currentMatches;
  }

  /** Changes the result and notifies listeners, as a real query does. */
  set(matches: boolean): void {
    if (matches === this.currentMatches) {
      return;
    }

    this.currentMatches = matches;

    const event = { matches, media: this.media } as MediaQueryListEvent;

    for (const listener of this.listeners) {
      if (typeof listener === 'function') {
        listener.call(this, event);
      } else {
        listener.handleEvent(event);
      }
    }
  }

  // Signatures kept wide enough to satisfy the DOM interface, which allows both
  // a handler function and an object with `handleEvent`.
  addEventListener(_type: string, listener: EventListenerOrEventListenerObject | null): void {
    if (listener) {
      this.listeners.add(listener);
    }
  }

  removeEventListener(_type: string, listener: EventListenerOrEventListenerObject | null): void {
    if (listener) {
      this.listeners.delete(listener);
    }
  }

  /** Deprecated DOM surface, present only to satisfy the interface. */
  addListener(): void {}
  removeListener(): void {}
  dispatchEvent(): boolean {
    return false;
  }
}

/** Serves fake media queries and lets a test change any of them. */
export class FakeMediaMatcher {
  private readonly lists = new Map<string, FakeMediaQueryList>();

  constructor(private readonly initial: Readonly<Record<string, boolean>> = {}) {}

  /** Supply this in place of MEDIA_QUERY_FACTORY. */
  readonly factory: MediaQueryFactory = (query) => {
    let list = this.lists.get(query);

    if (!list) {
      list = new FakeMediaQueryList(query, this.initial[query] ?? false);
      this.lists.set(query, list);
    }

    return list;
  };

  /** Simulates the environment changing: a theme switch, or a resize. */
  set(query: string, matches: boolean): void {
    this.factory(query);
    this.lists.get(query)?.set(matches);
  }
}

/**
 * An in-memory `Storage`.
 *
 * Tests must not share the real `localStorage`: it would leak state between
 * them and make results depend on execution order. It also lets a test seed a
 * persisted value without touching the browser.
 */
export function memoryStorage(seed: Readonly<Record<string, string>> = {}): Storage {
  const entries = new Map<string, string>(Object.entries(seed));

  return {
    get length() {
      return entries.size;
    },
    clear: () => entries.clear(),
    getItem: (key) => entries.get(key) ?? null,
    key: (index) => [...entries.keys()][index] ?? null,
    removeItem: (key) => {
      entries.delete(key);
    },
    setItem: (key, value) => {
      entries.set(key, value);
    },
  };
}

/** A store that refuses every operation, as a full or blocked one does. */
export function hostileStorage(): Storage {
  const refuse = (): never => {
    throw new DOMException('The operation is insecure.', 'SecurityError');
  };

  return {
    length: 0,
    clear: refuse,
    getItem: refuse,
    key: refuse,
    removeItem: refuse,
    setItem: refuse,
  };
}

/**
 * A permission client that answers from memory.
 *
 * <strong>Every spec that resolves a workspace needs one.</strong> Selecting a
 * workspace asks the server what the reader may do there, so a spec using
 * `HttpTestingController` would otherwise end with an unexpected open request
 * and fail on `verify()` for a reason that has nothing to do with what it
 * asserts.
 *
 * Passing no codes models a member who has been given nothing, which is a real
 * state rather than an edge case. Passing `'*'` grants everything, for the
 * specs that are about something other than permissions.
 */
export function permissionApiGranting(...codes: readonly string[]): {
  mine(): Observable<readonly string[]>;
  catalogue(): Observable<readonly PermissionDefinition[]>;
} {
  const granted = codes.includes('*') ? EVERY_PERMISSION.map((entry) => entry.code) : codes;

  return {
    mine: () => of(granted),
    catalogue: () => of(EVERY_PERMISSION),
  };
}

/**
 * The catalogue these doubles publish.
 *
 * A copy of the platform's own, rather than a fetch of it: a frontend spec must
 * not depend on the backend being reachable, and the codes are a published
 * contract that would be a breaking change to alter silently. The API tests are
 * what prove the two agree.
 */
export const EVERY_PERMISSION: readonly PermissionDefinition[] = [
  {
    code: 'workspace.view',
    group: 'Workspace',
    label: 'View workspace',
    description: "See the workspace's name and status.",
  },
  {
    code: 'ai-agents.view',
    group: 'AI agents',
    label: 'View AI agents',
    description: "See the workspace's AI agents and read their instructions.",
  },
  {
    code: 'ai-agents.create',
    group: 'AI agents',
    label: 'Create AI agents',
    description: 'Add a new AI agent to the workspace.',
  },
  {
    code: 'ai-agents.edit',
    group: 'AI agents',
    label: 'Edit AI agents',
    description: "Change an AI agent's name and instructions.",
  },
  {
    code: 'ai-agents.lifecycle',
    group: 'AI agents',
    label: 'Activate and disable AI agents',
    description: 'Decide whether an AI agent is live or taken out of service.',
  },
  {
    code: 'ai-agents.preview',
    group: 'AI agents',
    label: 'Preview AI agents',
    description: 'Try an AI agent out with a test message.',
  },
  {
    code: 'roles.view',
    group: 'Roles and permissions',
    label: 'View roles',
    description: "See the workspace's roles and which permissions each one grants.",
  },
  {
    code: 'roles.manage',
    group: 'Roles and permissions',
    label: 'Manage roles',
    description: 'Create custom roles and change their names and permissions.',
  },
  {
    code: 'members.view',
    group: 'Team members',
    label: 'View team members',
    description: 'See who belongs to this workspace and which roles they hold.',
  },
  {
    code: 'members.invite',
    group: 'Team members',
    label: 'Invite team members',
    description: 'Create and revoke invitations.',
  },
  {
    code: 'members.roles.manage',
    group: 'Team members',
    label: 'Assign roles to team members',
    description: 'Decide which roles a member holds.',
  },
  {
    code: 'conversations.view',
    group: 'Conversations',
    label: 'View conversations',
    description: 'Read the conversations customers have had with this workspace.',
  },
  {
    code: 'customers.view',
    group: 'Customers',
    label: 'View customers',
    description:
      'See who this workspace talks to, which channels they use and how much they have ' +
      'talked. It does not grant access to what they said.',
  },
  {
    code: 'customers.manage',
    group: 'Customers',
    label: 'Manage customers',
    description:
      'Name a customer, and combine two records that turn out to be the same person. ' +
      'Combining cannot be undone.',
  },
  {
    code: 'website-chat.view',
    group: 'Channels',
    label: 'View website chat',
    description: "See how this workspace's website chat is set up.",
  },
  {
    code: 'website-chat.manage',
    group: 'Channels',
    label: 'Manage website chat',
    description: 'Create website chat links and decide whether the public can use them.',
  },
  {
    code: 'knowledge.view',
    group: 'Knowledge',
    label: 'View knowledge',
    description:
      "Read the workspace's knowledge documents, see whether each one is indexed, and test " +
      'what an agent would retrieve for a question.',
  },
  {
    code: 'knowledge.manage',
    group: 'Knowledge',
    label: 'Manage knowledge',
    description:
      'Write and edit knowledge documents, index them, switch them on and off, and choose ' +
      'which AI agents may draw on them.',
  },
  {
    code: 'analytics.view',
    group: 'Analytics',
    label: 'View analytics',
    description:
      'See how many conversations the workspace handled, how its channels and AI agents are ' +
      'being used, and how much activity happened each day.',
  },
  {
    code: 'reports.view',
    group: 'Reports',
    label: 'View reports',
    description: 'See which reports exist and download generated ones.',
  },
  {
    code: 'reports.generate',
    group: 'Reports',
    label: 'Generate reports',
    description: 'Export the workspace numbers as CSV, Excel or PDF files.',
  },
  {
    code: 'reports.schedule',
    group: 'Reports',
    label: 'Schedule reports',
    description: 'Create weekly and monthly reports generated automatically.',
  },
  {
    code: 'billing.view',
    group: 'Billing',
    label: 'View billing',
    description: "See what this workspace's subscription is, what it costs and when it renews.",
  },
  {
    code: 'billing.manage',
    group: 'Billing',
    label: 'Manage billing',
    description:
      "Start a subscription and open the payment provider's portal to change payment " +
      'details or cancel. Viewing billing does not grant this.',
  },
];

/**
 * Gives every `<dialog>` under `root` the modal behaviour jsdom lacks.
 *
 * jsdom implements neither `showModal` nor `close`, so a confirmation would
 * throw before it could ask anything. These stand-ins do what the browser does
 * that the product relies on: mark the dialog open, and fire `close` when it
 * shuts.
 */
export function stubDialogs(root: HTMLElement): void {
  for (const dialog of root.querySelectorAll<HTMLDialogElement>('dialog')) {
    dialog.showModal = () => {
      dialog.setAttribute('open', '');
    };
    dialog.close = () => {
      dialog.removeAttribute('open');
      dialog.dispatchEvent(new Event('close'));
    };
  }
}

/**
 * Answers the confirmation dialog that is currently open under `root`.
 *
 * Presses the dialog's own buttons, so what is proven is the path a reader
 * takes, not a method call behind their back.
 */
export function answerConfirmation(root: HTMLElement, accept = true): void {
  const dialog = root.querySelector<HTMLDialogElement>('dialog[open]');

  if (!dialog) {
    throw new Error('No confirmation dialog is open.');
  }

  const buttons = [...dialog.querySelectorAll<HTMLButtonElement>('.confirm-actions button')];

  // Cancel is first in the markup, the confirming action last.
  (accept ? buttons[buttons.length - 1] : buttons[0]).click();
}

/**
 * The visitors' realtime service, without a socket (Phase 36.9).
 *
 * The real one loads the SignalR client on first use, which a unit test has no
 * reason to wait for: it counts who asked to listen and when, and connects to
 * nothing. Provided through DI, never `vi.mock` (the runner shares workers
 * between spec files, Phase 36.7).
 */
export class FakePublicChatRealtime {
  readonly live = signal(false);
  readonly listens: Array<{ onChanged: () => void }> = [];
  stops = 0;

  async listen(_mintTicket: unknown, onChanged: () => void): Promise<void> {
    this.listens.push({ onChanged });
  }

  async stop(): Promise<void> {
    this.stops += 1;
  }
}
