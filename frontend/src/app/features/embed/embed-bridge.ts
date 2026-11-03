import { Injectable, OnDestroy, signal } from '@angular/core';
import type { LauncherPosition } from './embed-chat-api';

/** The protocol namespace. Version 1, and the version is part of every name. */
const PROTOCOL = 'omniai:v1:';

/** Parent to iframe: the handshake. Its `event.origin` is the real parent. */
const INIT = PROTOCOL + 'init';

/** Parent to iframe: how much room there is. */
const VIEWPORT = PROTOCOL + 'viewport';

/** Iframe to parent: the widget has loaded and wants this corner. */
const READY = PROTOCOL + 'ready';

/** Iframe to parent: make room for the panel. */
const OPEN = PROTOCOL + 'open';

/** Iframe to parent: shrink back to the launcher. */
const CLOSE = PROTOCOL + 'close';

/**
 * How long the widget waits to be spoken to before deciding it is not embedded.
 *
 * The loader retries its handshake, so this only ever expires when there is no
 * loader at all — somebody opening the iframe address directly.
 */
const HANDSHAKE_TIMEOUT_MS = 5_000;

/**
 * The two-message conversation between the widget and the page hosting it.
 *
 * <strong>The parent speaks first, and that is the security design.</strong> The
 * widget has to know which website it is running on, and the only trustworthy
 * source is `event.origin` — filled in by the browser on a message the page
 * actually sent, and not forgeable by the page. `document.referrer` can be
 * suppressed, a query parameter in the iframe's own URL is chosen by whoever
 * wrote the embed code, and `window.parent.location` is unreadable across
 * origins. So the loader, which knows this iframe's origin from its own script
 * tag, sends the first message; everything after that is addressed to an origin
 * the browser vouched for.
 *
 * <strong>Nothing of substance crosses this channel, in either direction.</strong>
 * The parent learns which corner to use and whether the panel is open. It never
 * receives the credential, the conversation id, the workspace, the agent, any
 * knowledge, or a single word the customer or the AI said. All of that stays
 * inside this document, on the OmniAI origin, where the embedding website cannot
 * reach it.
 *
 * <strong>Every outbound message names the parent's exact origin.</strong> Never
 * `'*'`. A wildcard target would deliver to whatever document occupies the parent
 * frame at that moment, which on a page that can navigate itself is not
 * necessarily the one that spoke to us.
 */
@Injectable()
export class EmbedBridge implements OnDestroy {
  /** The parent's origin, once the browser has vouched for it. */
  private parentOrigin: string | null = null;

  /** Whether the host has told us it is short of room. */
  readonly compact = signal(false);

  private listener: ((event: MessageEvent) => void) | null = null;
  private timer: ReturnType<typeof setTimeout> | null = null;

  /**
   * Waits to be greeted, and reports the website this widget is embedded on.
   *
   * Resolves with `null` when nothing greets us, which means the address was
   * opened directly rather than embedded. That is not an error to report to a
   * server: there is no website to check, so there is nothing to ask about.
   */
  connect(): Promise<string | null> {
    if (window.parent === window.self) {
      // Not framed at all. Waiting five seconds to discover that would only make
      // the page look broken for five seconds.
      return Promise.resolve(null);
    }

    return new Promise<string | null>((resolve) => {
      const listener = (event: MessageEvent): void => {
        // Both halves matter. The source check rejects a message from any other
        // frame on the page; the origin check is what makes the value usable.
        if (event.source !== window.parent) {
          return;
        }

        const origin = this.readOrigin(event.origin);
        const type = this.readType(event.data);

        if (origin === null || type === null) {
          return;
        }

        if (type === INIT) {
          this.parentOrigin ??= origin;

          if (this.parentOrigin === origin) {
            this.settle(resolve, origin);
          }

          return;
        }

        // After the handshake, only the established parent is listened to. A
        // second frame shouting a viewport change cannot resize this one.
        if (type === VIEWPORT && origin === this.parentOrigin) {
          this.compact.set(this.readCompact(event.data));
        }
      };

      this.listener = listener;

      window.addEventListener('message', listener);

      this.timer = setTimeout(() => this.settle(resolve, null), HANDSHAKE_TIMEOUT_MS);
    });
  }

  /** Tells the host the widget has loaded, and which corner it belongs in. */
  ready(position: LauncherPosition): void {
    this.post({ type: READY, position });
  }

  /** Asks the host for panel-sized room. */
  open(): void {
    this.post({ type: OPEN });
  }

  /** Asks the host to shrink back to the launcher. */
  close(): void {
    this.post({ type: CLOSE });
  }

  ngOnDestroy(): void {
    if (this.listener) {
      window.removeEventListener('message', this.listener);
      this.listener = null;
    }

    this.clearTimer();
  }

  /**
   * Finishes the handshake once, whichever way it ended.
   *
   * The listener stays attached, because viewport messages keep arriving for the
   * lifetime of the widget.
   */
  private settle(resolve: (origin: string | null) => void, origin: string | null): void {
    this.clearTimer();
    resolve(origin);
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer);
      this.timer = null;
    }
  }

  /**
   * A website address this widget is willing to name to the server.
   *
   * `event.origin` is `"null"` for a sandboxed or `data:` document, which is an
   * opaque origin: it identifies nothing, so no allowlist entry can ever match it
   * and treating it as a website would be a lie. Anything that is not a normal
   * http(s) origin is refused for the same reason.
   */
  private readOrigin(value: string): string | null {
    if (!value || value === 'null') {
      return null;
    }

    try {
      const parsed = new URL(value);

      return (parsed.protocol === 'https:' || parsed.protocol === 'http:') &&
        parsed.origin === value
        ? value
        : null;
    } catch {
      return null;
    }
  }

  /** The message name, if this is a message in our protocol at all. */
  private readType(data: unknown): string | null {
    if (data === null || typeof data !== 'object') {
      return null;
    }

    const type: unknown = (data as { type?: unknown }).type;

    return typeof type === 'string' && type.startsWith(PROTOCOL) ? type : null;
  }

  private readCompact(data: unknown): boolean {
    return (data as { compact?: unknown }).compact === true;
  }

  /**
   * Sends one structural message to the established parent.
   *
   * Silent before the handshake, because before it there is no origin to name and
   * the alternative — `'*'` — is the one thing this channel must never do.
   */
  private post(message: { type: string; position?: string }): void {
    if (this.parentOrigin === null) {
      return;
    }

    window.parent.postMessage(message, this.parentOrigin);
  }
}
