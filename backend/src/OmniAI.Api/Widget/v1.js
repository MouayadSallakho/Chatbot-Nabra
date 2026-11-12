/*
 * Nabra website chat loader, version 1.
 *
 * WHAT THIS FILE IS ALLOWED TO DO, AND NOTHING MORE.
 *
 * It runs on somebody else's website. That is the whole reason it is this small
 * and this boring: it creates one iframe, positions it, and relays a two-message
 * protocol. Everything a visitor can see or type lives inside the iframe, on a
 * Nabra origin, where the host page's CSS cannot reach it and the host page's
 * JavaScript cannot read it.
 *
 * It never uses eval, never uses document.write, never builds HTML from a string,
 * and never runs anything a workspace configured. A tenant chooses a colour and a
 * corner; a tenant does not choose code that runs on their customers' machines.
 *
 * It never sees a conversation credential. The credential is issued to the
 * iframe, by the API, over the iframe's own origin, and stays there.
 *
 * No build step, no dependencies, no framework, ES5-compatible syntax so it runs
 * wherever the customer's site runs.
 */
(function () {
  'use strict';

  var PROTOCOL = 'omniai:v1:';
  var READY = PROTOCOL + 'ready';
  var OPEN = PROTOCOL + 'open';
  var CLOSE = PROTOCOL + 'close';
  var INIT = PROTOCOL + 'init';
  var VIEWPORT = PROTOCOL + 'viewport';

  /*
   * The loader picks the dimensions, and the iframe only says which of two
   * states it is in.
   *
   * An iframe that could ask for arbitrary width and height would be an iframe
   * that could cover the host page — a full-screen overlay on somebody else's
   * site, from a script they added for a chat bubble. OPEN and CLOSE are the
   * whole vocabulary, and these numbers are the answer to them.
   */
  var CLOSED_SIZE = 84;
  var PANEL_WIDTH = 400;
  var PANEL_HEIGHT = 640;
  var EDGE = 20;

  /* Below this the panel goes near-full-screen, because a 400px floating card on
   * a 390px phone is a card with no room to be in. */
  var COMPACT_BREAKPOINT = 480;

  /* How often the handshake repeats, and for how long. Ten seconds is far longer
   * than any widget takes to start, and the retries stop the moment it answers. */
  var HANDSHAKE_INTERVAL_MS = 250;
  var HANDSHAKE_ATTEMPTS = 40;

  /* z-index high enough to sit above ordinary page furniture, and deliberately
   * below the maximum so a site's own modal can still cover it. */
  var LAYER = 2147483000;

  /*
   * The host page's languages the frame is told about (Phase 38.7a), and the
   * <html lang> values that mean each: the language itself, alone or with a
   * region ("ar", "ar-SA", "en", "en-GB"). Any other value, or none, tells the
   * frame nothing, and it follows the visitor's browser as it always did.
   */
  var FRAME_LANGUAGES = [['ar', /^ar(-|$)/i], ['en', /^en(-|$)/i]];

  var script = document.currentScript;

  if (!script) {
    return;
  }

  /*
   * The workspace's public key. Snippets issued from Phase 37.1 carry it as
   * data-nabra-key; every snippet pasted before then carries data-omniai-key,
   * and those sites must keep working without anybody editing them. When one
   * tag carries both, the newer name wins.
   */
  var publicKey = script.getAttribute('data-nabra-key') || script.getAttribute('data-omniai-key');

  if (!publicKey) {
    return;
  }

  /*
   * One widget per page, whatever the page does.
   *
   * Tag managers add the same script twice, single-page apps re-run it on
   * navigation, and a CMS can include a snippet in both a template and a page.
   * A second iframe would mean two launchers and two conversations, so the
   * second instance stops here.
   */
  if (window.__omniaiWidgetLoaded) {
    return;
  }

  window.__omniaiWidgetLoaded = true;

  /* The origin this script was served from is the origin the iframe belongs to.
   * Derived from src rather than configured, so a deployment on any hostname
   * produces a snippet that works there. */
  var origin = new URL(script.src, window.location.href).origin;
  var language = frameLanguage();
  var frameUrl = origin + '/embed/' + encodeURIComponent(publicKey) + (language ? '?lang=' + language : '');

  var frame = document.createElement('iframe');

  /* A real title. A frame announced as "iframe" is a frame a screen-reader user
   * has no reason to enter. */
  frame.title = frameTitle();
  frame.src = frameUrl;

  /*
   * allow-same-origin keeps the iframe on the Nabra origin, which is what lets
   * it call its own API and use its own sessionStorage. It grants the iframe
   * nothing over this page: the two are different origins, so the browser keeps
   * them apart regardless.
   *
   * Everything else is withheld. No forms, no popups, no top-level navigation,
   * no downloads, no pointer lock. A chat panel needs none of them, and a widget
   * that could navigate the host page away would be a widget that could be used
   * to phish the site's own visitors.
   */
  frame.setAttribute('sandbox', 'allow-scripts allow-same-origin');

  /* Nothing. Camera, microphone and geolocation are denied by default in a
   * cross-origin frame; saying so explicitly means a future default cannot
   * quietly grant one. */
  frame.setAttribute('allow', '');

  /* The origin only. The host page's path can carry an order number, a search
   * term or a customer identifier, and none of that is Nabra's business. */
  frame.setAttribute('referrerpolicy', 'strict-origin');

  frame.setAttribute('aria-hidden', 'false');

  var style = frame.style;

  style.position = 'fixed';
  style.border = '0';
  style.margin = '0';
  style.padding = '0';
  style.colorScheme = 'normal';
  style.background = 'transparent';
  style.zIndex = String(LAYER);
  /* The closed launcher is a circle with nothing behind it, so the page shows
   * through the corners of the frame. */
  style.display = 'block';

  /* The configured corner, as the iframe reported it: Left, Right, Start or End.
   * Start and End are resolved against the page on every layout (see corner). */
  var position = 'Right';
  var open = false;

  /* The repeating handshake, while it is running. */
  var handshake = null;

  /*
   * The frame's accessible name, in the language of the page it sits on.
   *
   * The host page's own <html lang> decides, because that is the language its
   * visitor is reading; the browser's language stands in only when the page
   * declares none. Any Arabic tag ("ar", "ar-SA") gets the Arabic word, every
   * other language the English one. Written as escapes so the word survives a
   * host page that declares some other character set.
   */
  function frameTitle() {
    var lang = document.documentElement.getAttribute('lang') || window.navigator.language || '';

    /* "الدردشة": chat. */
    return /^ar(-|$)/i.test(lang) ? '\u0627\u0644\u062F\u0631\u062F\u0634\u0629' : 'Chat';
  }

  /*
   * The language to ask the frame for: the host page's own <html lang>, read the
   * way the title reads it, and only when it is one of FRAME_LANGUAGES (Phase
   * 38.7a). Unlike the title there is no fallback to the browser's language: an
   * empty answer adds nothing to the frame's address, and the frame then decides
   * from the visitor's browser itself. One word, from a fixed list, is all that
   * reaches the frame from the host page.
   */
  function frameLanguage() {
    var lang = document.documentElement.getAttribute('lang') || '';

    for (var index = 0; index < FRAME_LANGUAGES.length; index += 1) {
      if (FRAME_LANGUAGES[index][1].test(lang)) {
        return FRAME_LANGUAGES[index][0];
      }
    }

    return '';
  }

  function compact() {
    return window.innerWidth <= COMPACT_BREAKPOINT;
  }

  /*
   * The direction the host page reads in: its own <html dir> when it declares
   * one, else the direction the browser computed for it. Anything but rtl is
   * left-to-right, which is what a page with no direction at all is.
   */
  function pageDirection() {
    var declared = (document.documentElement.getAttribute('dir') || '').toLowerCase();

    if (declared === 'rtl' || declared === 'ltr') {
      return declared;
    }

    var computed = window.getComputedStyle
      ? window.getComputedStyle(document.documentElement).direction
      : '';

    return computed === 'rtl' ? 'rtl' : 'ltr';
  }

  /*
   * The physical corner the configured position means on this page (Phase 37.5a).
   *
   * Left and Right are corners already, exactly as before. Start is the corner
   * where the page's text starts and End where it ends, so one workspace's
   * launcher sits in the same reading position on its English and its Arabic
   * site. Resolved on every layout, so a page that switches language in place
   * moves the launcher with it.
   */
  function corner() {
    if (position === 'Left') {
      return 'left';
    }

    if (position === 'Start' || position === 'End') {
      var rtl = pageDirection() === 'rtl';

      return (position === 'Start') === rtl ? 'right' : 'left';
    }

    return 'right';
  }

  /*
   * Applies the one layout that matches the current state.
   *
   * Called on every state change and on resize, so rotating a phone or opening
   * the on-screen keyboard re-measures rather than leaving a panel half off the
   * screen.
   */
  function layout() {
    var small = compact();
    var side = corner();
    var other = side === 'left' ? 'right' : 'left';

    style[other] = 'auto';

    if (!open) {
      style.width = CLOSED_SIZE + 'px';
      style.height = CLOSED_SIZE + 'px';
      style.bottom = '0px';
      style[side] = '0px';
      style.maxWidth = '100vw';
      style.maxHeight = '100vh';

      return;
    }

    if (small) {
      /* Near-full-screen, inset slightly so the page is still visibly behind it
       * and the visitor can tell they have not navigated away. */
      style.width = '100vw';
      style.height = '100%';
      style.bottom = '0px';
      style.left = '0px';
      style.right = 'auto';
      style.maxWidth = '100vw';
      style.maxHeight = '100vh';

      return;
    }

    /* Never taller than the viewport: a 640px panel in a 500px window would run
     * off the top of the screen with its header out of reach. */
    var height = Math.min(PANEL_HEIGHT, window.innerHeight - EDGE * 2);
    var width = Math.min(PANEL_WIDTH, window.innerWidth - EDGE * 2);

    style.width = width + 'px';
    style.height = height + 'px';
    style.bottom = EDGE + 'px';
    style[side] = EDGE + 'px';
    style.maxWidth = '100vw';
    style.maxHeight = '100vh';
  }

  /* Tells the iframe how much room it has, so it can choose a compact layout.
   * Structural only: a number of pixels and a boolean. */
  function sendViewport() {
    post({ type: VIEWPORT, compact: compact() });
  }

  /*
   * Every message to the iframe names its exact origin.
   *
   * Never '*'. A wildcard target would deliver to whatever document happened to
   * occupy that frame, which on a page that can navigate it is not necessarily
   * the one this loader created.
   */
  function post(message) {
    if (frame.contentWindow) {
      frame.contentWindow.postMessage(message, origin);
    }
  }

  function onMessage(event) {
    /* Both halves matter. The origin check alone would accept a message from any
     * other Nabra frame on the page; the source check alone would accept a
     * message from this frame after it had been navigated elsewhere. */
    if (event.origin !== origin || event.source !== frame.contentWindow) {
      return;
    }

    var data = event.data;

    if (!data || typeof data !== 'object' || typeof data.type !== 'string') {
      return;
    }

    if (data.type.indexOf(PROTOCOL) !== 0) {
      return;
    }

    if (data.type === READY) {
      stopHandshake();

      /* The only field the loader reads from the iframe, and it is one of four
       * words. Anything else is ignored and the default stands. */
      position = data.position === 'Left' || data.position === 'Start' || data.position === 'End'
        ? data.position
        : 'Right';
      layout();
      sendViewport();

      return;
    }

    if (data.type === OPEN) {
      open = true;
      layout();

      return;
    }

    if (data.type === CLOSE) {
      open = false;
      layout();
    }

    /* Anything else in the protocol namespace is a message from a newer widget
     * than this loader understands. Ignoring it is correct: a v1 loader must not
     * guess at v2 semantics. */
  }

  function start() {
    layout();

    (document.body || document.documentElement).appendChild(frame);

    window.addEventListener('message', onMessage);
    window.addEventListener('resize', function () {
      layout();
      sendViewport();
    });

    /*
     * The handshake, and the reason it starts here rather than in the iframe.
     *
     * The iframe must learn the real parent origin from event.origin, which the
     * browser fills in and a page cannot forge. For that to happen the parent has
     * to speak first — and the parent can, because it knows the iframe's origin
     * from this script's own src. The alternative, the iframe guessing the parent
     * from document.referrer or from a query parameter, would be trusting a value
     * the embedding page controls.
     */
    frame.addEventListener('load', startHandshake);
  }

  /*
   * Says hello until the widget says hello back.
   *
   * The frame's load event and the widget's own start-up are not ordered with
   * respect to each other: a framework that boots asynchronously can finish
   * attaching its listener after load has already fired, and a single INIT sent
   * at that moment would land on nobody. Repeating it costs one structural
   * message every quarter second for at most ten seconds, and stops the instant
   * the widget answers.
   */
  function startHandshake() {
    stopHandshake();

    post({ type: INIT });

    var attempts = 0;

    handshake = window.setInterval(function () {
      attempts += 1;

      if (attempts > HANDSHAKE_ATTEMPTS) {
        stopHandshake();

        return;
      }

      post({ type: INIT });
    }, HANDSHAKE_INTERVAL_MS);
  }

  function stopHandshake() {
    if (handshake !== null) {
      window.clearInterval(handshake);
      handshake = null;
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', start);
  } else {
    start();
  }
})();
