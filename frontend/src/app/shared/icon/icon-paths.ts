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
  /** Sounds: a speaker and one wave. */
  volume: ['M4 10v4h3l5 4V6l-5 4H4z', 'M16 9.5a3.5 3.5 0 0 1 0 5'],

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
  /** Customers: one person, distinct from the two-person Team glyph. */
  customers: ['M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8z', 'M4.5 20.5a7.5 7.5 0 0 1 15 0'],

  /** Reports: a document with a chart on it. */
  reports: [
    'M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z',
    'M14 3v5h5',
    'M9 17v-3',
    'M12 17v-5',
    'M15 17v-2',
  ],

  /** Plan and usage: a gauge. */
  usage: ['M4.5 17a8.5 8.5 0 1 1 15 0', 'M12 13.5l3.5-4', 'M12 15.5a2 2 0 1 0 0-4 2 2 0 0 0 0 4z'],

  /** Billing: a card. */
  billing: ['M3 6h18v12H3z', 'M3 10h18', 'M7 15h4'],

  /** Roles and permissions: a shield with a tick. */
  roles: ['M12 3l8 3v6c0 4.5-3.4 8.3-8 9-4.6-.7-8-4.5-8-9V6z', 'M8.5 12l2.5 2.5 4.5-5'],

  /** Keep the sidebar open: a pin. */
  pin: ['M9 4h6', 'M10 4v6l-3 4h10l-3-4V4', 'M12 14v6'],

  /** Overflow: three dots in a row. */
  more: ['M5.5 12h.01', 'M12 12h.01', 'M18.5 12h.01'],

  /** Search: a lens. */
  search: ['M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14z', 'M20 20l-4-4'],

  /** Sign out: leaving through a door. */
  'sign-out': ['M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4', 'M15 16l4-4-4-4', 'M19 12H9'],

  /** Switch between workspaces: two opposing arrows. */
  switch: ['M7 4L4 7l3 3', 'M4 7h13', 'M17 20l3-3-3-3', 'M20 17H7'],

  /** Opens a menu or list. */
  'chevron-down': ['M6 9l6 6 6-6'],

  /** Back, against the reading direction. */
  'arrow-start': ['M19 12H5', 'M11 6l-6 6 6 6'],

  /** Reload the data on screen. */
  refresh: [
    'M20 11a8 8 0 0 0-14.7-4.4L4 8',
    'M4 3v5h5',
    'M4 13a8 8 0 0 0 14.7 4.4L20 16',
    'M20 21v-5h-5',
  ],

  /** Download a file. */
  download: ['M12 4v11', 'M7 10l5 5 5-5', 'M5 20h14'],

  /** Add something new. */
  plus: ['M12 5v14', 'M5 12h14'],

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

  /** Reduce transparency: two overlapping panes, the front one solid-edged. */
  layers: ['M4 8h10v10H4z', 'M10 8V4h10v10h-6'],

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

  // --- Voices --------------------------------------------------------------
  // The two speakers of every conversation. Always drawn beside the word
  // ("AI", "Person"), never alone. The AI is a calm bot face -- never a
  // sparkle, which is the habitual and meaningless "AI" glyph.
  /** The AI voice. */
  ai: [
    'M7 8h10a3 3 0 0 1 3 3v5a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3v-5a3 3 0 0 1 3-3z',
    'M12 4.5V8',
    'M9.5 13.25h.01',
    'M14.5 13.25h.01',
  ],

  /** A person on the team. */
  person: ['M12 11.5a3.75 3.75 0 1 0 0-7.5 3.75 3.75 0 0 0 0 7.5z', 'M5 20a7 7 0 0 1 14 0'],

  // --- Channels ------------------------------------------------------------
  // Generic glyphs, never recoloured brand logos, and always shown with the
  // channel's name as a word.
  /** Website chat: a browser window. */
  website: [
    'M3.5 6.5a2 2 0 0 1 2-2h13a2 2 0 0 1 2 2v11a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z',
    'M3.5 9h17',
    'M6.5 6.75h.01',
    'M9 6.75h.01',
  ],

  /** WhatsApp, generically: a phone handset inside a speech bubble. */
  whatsapp: [
    'M12 3.5a8.5 8.5 0 0 0-7.3 12.85L3.5 20.5l4.3-1.15A8.5 8.5 0 1 0 12 3.5z',
    'M9.4 8.8c.3-.35.75-.35.95 0l.75 1.4c.12.25.05.55-.15.75l-.45.45a4.6 4.6 0 0 0 2.1 2.1l.45-.45c.2-.2.5-.27.75-.15l1.4.75c.35.2.35.65 0 .95-.65.6-1.55.85-2.4.5a7.4 7.4 0 0 1-3.9-3.9c-.35-.85-.1-1.75.5-2.4z',
  ],

  /** Instagram, generically: a camera. */
  instagram: [
    'M4 9a2.5 2.5 0 0 1 2.5-2.5H8L9.5 4.5h5L16 6.5h1.5A2.5 2.5 0 0 1 20 9v7.5a2.5 2.5 0 0 1-2.5 2.5h-11A2.5 2.5 0 0 1 4 16.5z',
    'M12 16a3.25 3.25 0 1 0 0-6.5 3.25 3.25 0 0 0 0 6.5z',
  ],

  /** Messenger, generically: two overlapping speech bubbles. */
  messenger: [
    'M3.5 5.5a2 2 0 0 1 2-2h7a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2H8l-4.5 3.5z',
    'M17 9h1.5a2 2 0 0 1 2 2v8.5L17 17h-4.5a2 2 0 0 1-2-2v-.5',
  ],

  /** Under construction: this area is not built yet. */
  blueprint: ['M4 4h16v16H4z', 'M4 9h16', 'M9 9v11', 'M13 13h4', 'M13 16.5h4'],

  // --- Charts and reports (Phase 36.7) -----------------------------------------
  /** A chart read as a table: a frame, a header row and one column rule. */
  table: ['M4 5h16v14H4z', 'M4 10h16', 'M4 14.5h16', 'M10 10v9'],

  /** Waiting or generating: a clock face. Symmetric enough never to mirror. */
  clock: ['M21 12a9 9 0 1 0-18 0 9 9 0 0 0 18 0z', 'M12 7v5l3 2'],

  /** A schedule: a calendar page with two rings. */
  calendar: ['M4 6h16v14H4z', 'M4 10h16', 'M8 3v4', 'M16 3v4'],

  // --- Knowledge files (Phase 36.8) ---------------------------------------------
  /** A file: a page with a folded corner and two lines of text. Never mirrors. */
  file: ['M14 3H6v18h12V7z', 'M14 3v4h4', 'M9 12.5h6', 'M9 16h6'],

  /** Upload: an arrow rising out of a tray. */
  upload: ['M12 15V4', 'M7 9l5-5 5 5', 'M5 20h14'],
} as const;

/** Every glyph the product can draw. */
export type IconName = keyof typeof ICON_PATHS;

/**
 * The glyphs that point along the reading direction, and so turn round in
 * Arabic: back and forward arrows, start and end chevrons, and leaving (the
 * door is behind, the arrow goes the way the reader goes).
 *
 * Everything else never mirrors: the mark, the two voices, channel glyphs,
 * status signs, the check, search, refresh, download, the clock-like and the
 * symmetric ones (localisation-arabic-rtl.md, 4.3). A new directional glyph --
 * send, reply, undo -- is added here, not flipped at its use site.
 */
export const MIRRORED_ICONS: ReadonlySet<IconName> = new Set<IconName>([
  'arrow-start',
  'arrow-end',
  'chevron-start',
  'chevron-end',
  'sign-out',
]);
