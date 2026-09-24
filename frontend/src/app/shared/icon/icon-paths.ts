/**
 * The product's icon set.
 *
 * WHY THIS IS NOT A PACKAGE. The application needs two dozen glyphs. Every icon
 * library worth using ships hundreds, and pulling one in for this would add a
 * dependency to audit and upgrade in exchange for shipping mostly dead weight.
 * Authoring the set instead makes the stroke language a fact of the codebase
 * rather than something reviewers have to police.
 *
 * ONE VISUAL GRAMMAR, enforced by construction: every glyph is drawn on the same
 * 24x24 grid, as outline strokes only, with no fills, round caps and round
 * joins, and a single shared stroke width. Nothing here can be a filled glyph
 * from one family sitting next to a hairline from another, because the geometry
 * is all that is stored -- presentation is fixed by the component.
 *
 * STORED AS PATH DATA, not as markup. Rendering is `<path [attr.d]="d">` over
 * this array, so no SVG source is ever passed through `innerHTML` and no
 * sanitiser bypass exists anywhere in the icon layer. An icon set is exactly the
 * kind of "obviously safe" content that becomes an injection point the first
 * time somebody makes it configurable.
 */
export const ICON_PATHS = {
  // --- Navigation ----------------------------------------------------------
  /** Overview: a four-panel dashboard. */
  overview: ['M4 4h6v6H4z', 'M14 4h6v6h-6z', 'M14 14h6v6h-6z', 'M4 14h6v6H4z'],

  /** AI agents: a bot with an antenna. */
  agents: [
    'M12 3v3',
    'M5 8h14a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2z',
    'M9 13v1.5',
    'M15 13v1.5',
  ],

  /** Conversations: a speech bubble. */
  conversations: ['M21 15a2 2 0 0 1-2 2H8l-4 4V5a2 2 0 0 1 2-2h13a2 2 0 0 1 2 2z'],

  /** Knowledge: an open book. */
  knowledge: [
    'M4 4.5A2.5 2.5 0 0 1 6.5 2H20v15H6.5A2.5 2.5 0 0 0 4 19.5z',
    'M4 19.5A2.5 2.5 0 0 1 6.5 17H20v5H6.5A2.5 2.5 0 0 1 4 19.5z',
  ],

  /** Channels: connected endpoints. */
  channels: [
    'M21 5a3 3 0 1 0-6 0 3 3 0 0 0 6 0z',
    'M9 12a3 3 0 1 0-6 0 3 3 0 0 0 6 0z',
    'M21 19a3 3 0 1 0-6 0 3 3 0 0 0 6 0z',
    'M8.6 13.5l6.8 4',
    'M15.4 6.5l-6.8 4',
  ],

  /** Integrations: a connector. */
  integrations: ['M9 2v6', 'M15 2v6', 'M6 8h12v3a6 6 0 0 1-12 0z', 'M12 17v5'],

  /** Analytics: a bar chart. */
  analytics: ['M3 20h18', 'M6 20V11', 'M12 20V4', 'M18 20v-6'],

  /** Settings: sliders. */
  settings: [
    'M4 7h10',
    'M18 7h2',
    'M4 17h4',
    'M12 17h8',
    'M18 7a2 2 0 1 0-4 0 2 2 0 0 0 4 0z',
    'M12 17a2 2 0 1 0-4 0 2 2 0 0 0 4 0z',
  ],

  /** Team: two people, the nearer one in front. */
  team: [
    'M16 20v-1.5a3.5 3.5 0 0 0-3.5-3.5h-5A3.5 3.5 0 0 0 4 18.5V20',
    'M10 11.5a3.25 3.25 0 1 0 0-6.5 3.25 3.25 0 0 0 0 6.5z',
    'M20 20v-1.5a3.5 3.5 0 0 0-2.6-3.38',
    'M15.5 5.22a3.25 3.25 0 0 1 0 6.06',
  ],

  // --- Shell controls ------------------------------------------------------
  /** Open the navigation drawer. */
  menu: ['M4 7h16', 'M4 12h16', 'M4 17h16'],

  /** Dismiss. */
  close: ['M6 6l12 12', 'M18 6L6 18'],

  /** Points towards the start edge: collapses the sidebar in a left-to-right layout. */
  'chevron-start': ['M15 5l-7 7 7 7'],

  /** Points towards the end edge: expands it again. */
  'chevron-end': ['M9 5l7 7-7 7'],

  // --- Theme ---------------------------------------------------------------
  sun: [
    'M16 12a4 4 0 1 0-8 0 4 4 0 0 0 8 0z',
    'M12 2v2',
    'M12 20v2',
    'M4.9 4.9l1.4 1.4',
    'M17.7 17.7l1.4 1.4',
    'M2 12h2',
    'M20 12h2',
    'M6.3 17.7l-1.4 1.4',
    'M19.1 4.9l-1.4 1.4',
  ],

  moon: ['M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5z'],

  /** Follow the operating system: a display. */
  monitor: ['M4 5h16v10H4z', 'M9 19h6', 'M12 15v4'],

  // --- States --------------------------------------------------------------
  /** Nothing here yet. */
  inbox: [
    'M20 13h-4l-1.5 3h-5L8 13H4',
    'M6.2 4.5h11.6a2 2 0 0 1 1.9 1.4L22 13v5a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2v-5l2.3-7.1a2 2 0 0 1 1.9-1.4z',
  ],

  /** Something went wrong. Also the Warning severity of a notification. */
  alert: ['M12 4L2.5 20h19z', 'M12 10v4', 'M12 17h.01'],

  /** Critical severity: a stop sign, distinct from the warning triangle by shape. */
  critical: ['M8.3 3h7.4L21 8.3v7.4L15.7 21H8.3L3 15.7V8.3z', 'M12 8v5', 'M12 16.5h.01'],

  /** Information severity: a circled i. */
  info: ['M21 12a9 9 0 1 0-18 0 9 9 0 0 0 18 0z', 'M12 11v5', 'M12 7.5h.01'],

  /** Done: mark as read. */
  check: ['M5 12.5l4.5 4.5L19 7.5'],

  // --- Notifications --------------------------------------------------------
  /** The notification centre. */
  bell: ['M18 16V11a6 6 0 1 0-12 0v5l-2 2h16z', 'M10 20.5a2 2 0 0 0 4 0'],

  /** Continue, in the reading direction. */
  'arrow-end': ['M5 12h14', 'M13 6l6 6-6 6'],

  /** Under construction: this area is not built yet. */
  blueprint: ['M4 4h16v16H4z', 'M4 9h16', 'M9 9v11', 'M13 13h4', 'M13 16.5h4'],
} as const;

/** Every glyph the product can draw. */
export type IconName = keyof typeof ICON_PATHS;
