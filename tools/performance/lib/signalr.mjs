// Dependency-free SignalR JSON-protocol client over Node's built-in WebSocket.
//
// NON-PRODUCTION load tooling. It exists so hundreds of headless hub connections
// can be opened without adding @microsoft/signalr (or any package) to the
// repository. It implements only what a load client needs: negotiate, the JSON
// handshake, invocations with completions, keep-alive pings and event capture.
//
// The session cookie is sent on the negotiate request and the WebSocket upgrade,
// exactly as a browser would; it is never printed.

import { loopbackBase } from './target.mjs';

const RS = '\u001e';

export class HubConnection {
  /**
   * @param {string} base Loopback API base URL.
   * @param {string} hubPath Hub route, e.g. /hubs/workspace.
   * @param {() => string} cookieHeader Returns the Cookie header to send.
   */
  constructor(base, hubPath, cookieHeader) {
    this.base = loopbackBase(base);
    this.hubPath = hubPath;
    this.cookieHeader = cookieHeader;
    this.handlers = new Map();
    this.pending = new Map();
    this.nextId = 1;
    this.closed = false;
    this.events = 0;
    this.onclose = null;
  }

  on(target, handler) {
    this.handlers.set(target, handler);
  }

  async start(timeoutMs = 15000) {
    const started = performance.now();
    const negotiate = await fetch(`${this.base}${this.hubPath}/negotiate?negotiateVersion=1`, {
      method: 'POST',
      headers: { cookie: this.cookieHeader(), origin: this.base },
    });

    if (negotiate.status !== 200) {
      throw new Error(`negotiate -> ${negotiate.status}`);
    }

    const body = await negotiate.json();
    const token = body.connectionToken ?? body.connectionId;
    const wsUrl = `${this.base.replace(/^http/, 'ws')}${this.hubPath}?id=${encodeURIComponent(token)}`;

    await new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error('handshake timeout')), timeoutMs);
      // Node's (undici) WebSocket accepts non-standard headers in its init object.
      const ws = new WebSocket(wsUrl, { headers: { cookie: this.cookieHeader(), origin: this.base } });
      this.ws = ws;
      let handshaken = false;

      ws.onopen = () => ws.send(JSON.stringify({ protocol: 'json', version: 1 }) + RS);
      ws.onerror = () => {
        if (!handshaken) {
          clearTimeout(timer);
          reject(new Error('websocket error'));
        }
      };
      ws.onclose = (event) => {
        this.closed = true;
        clearInterval(this.pinger);

        for (const { reject: fail } of this.pending.values()) {
          fail(new Error('connection closed'));
        }

        this.pending.clear();

        if (!handshaken) {
          clearTimeout(timer);
          reject(new Error(`closed during handshake (${event.code})`));
        }

        this.onclose?.(event);
      };
      ws.onmessage = (event) => {
        const frames = String(event.data).split(RS).filter(Boolean);

        for (const frame of frames) {
          const message = JSON.parse(frame);

          if (!handshaken) {
            handshaken = true;
            clearTimeout(timer);

            if (message.error) {
              reject(new Error(`handshake refused: ${message.error}`));
            } else {
              resolve();
            }

            continue;
          }

          this.dispatch(message);
        }
      };
    });

    // Keep-alive: the server drops clients that stay silent past its client timeout.
    this.pinger = setInterval(() => this.send({ type: 6 }), 15000);
    this.connectMs = performance.now() - started;

    return this.connectMs;
  }

  dispatch(message) {
    switch (message.type) {
      case 1: {
        this.events++;
        this.handlers.get(message.target)?.(...(message.arguments ?? []));
        break;
      }
      case 3: {
        const waiter = this.pending.get(message.invocationId);

        if (waiter) {
          this.pending.delete(message.invocationId);

          if (message.error) {
            waiter.reject(new Error(message.error));
          } else {
            waiter.resolve(message.result);
          }
        }

        break;
      }
      case 7: {
        this.closeError = message.error ?? null;
        break;
      }
      default:
        break;
    }
  }

  send(message) {
    if (!this.closed && this.ws?.readyState === 1) {
      this.ws.send(JSON.stringify(message) + RS);
    }
  }

  invoke(target, ...args) {
    const invocationId = String(this.nextId++);

    return new Promise((resolve, reject) => {
      this.pending.set(invocationId, { resolve, reject });
      this.send({ type: 1, invocationId, target, arguments: args });
    });
  }

  stop() {
    clearInterval(this.pinger);

    if (!this.closed) {
      this.ws?.close(1000);
    }
  }
}
