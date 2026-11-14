# Phase 39.2b: foundations, the components (Calm Glass)

Step 39.2b of the approved plan (`PHASE_39_0_REDESIGN_PLAN_REPORT.md`, sections 3.2.3, 3.2.5, 3.6 and 12): the choice-control family with the global checkbox rule and `.switch` removed; buttons, chips, inputs, segmented controls, rows and avatars, bubbles, event lines, toasts, dialogs, menus, popovers, tooltips, the skeleton look, empty states, the orb and the glass surface; the new brand mark and the outlined lockup; the Design Lab and MASTER.md. 39.2a (tokens, fonts, the alias layer) is committed as `71f8e18`.

Evidence: `C:\Users\mouay\Desktop\nabra-frontend-evidence\p39-2b\` (`gates\`, `chrome\`, `shots\`, `e2e\`, `lint\`, `scratch\`).

## 1. Summary

- Every checkbox, radio button and switch in the product now has a visible 20 px box (a 40×24 track for switches) and a 44 px target that includes its words. The old 16 px boxes are gone, and so are the global checkbox rule and the `.switch` workaround. Nothing behaves differently.
- Buttons and inputs are now Calm Glass: pills and fields 44 px tall (40 px in dense places), weight 600, an emerald focus halo. Status badges became chips: a dot or glyph and a word.
- Menus, the notifications panel, the command palette, dialogs and toasts are glass. With "Reduce transparency", the device's own setting or Windows high contrast, they turn solid.
- The new two-circle mark is in place, and the loading screen shows the new lockup ("Nabra" and «نبرة», drawn as shapes so no extra font is downloaded).
- The design lab (development only) shows every component in both languages and themes. MASTER.md now describes Calm Glass end to end, with a catalogue of components and 60 newly measured colour pairs.

## 2. Starting point

- Branch `master`, HEAD `71f8e18` (Phase 39.2a, "feat: give every screen the Calm Glass colours and type, …"), worktree clean (`git status --short` empty). 39.1 (`9bbd4a3`) is below it.
- Dev stack up: `omniai-p36-api` with the AI stand-in, `omniai-p37-mailpit`, `omniai-p37-netns`, `omniai_pg25`, `omniai_redis18`; `/health/ready` 200; the dev server on :4200 answering. Provider canary PASS before any traffic (`gates\canary-start.log`).

## 3. Changes by area

`git status --short` at the end of the step:

```text
 M docs/architecture/admin-design-system.md
 M docs/design/MASTER.md
 M frontend/src/app/core/boot/boot-state.ts
 M frontend/src/app/core/brand/brand.spec.ts
 M frontend/src/app/core/feedback/confirm-dialog.scss
 M frontend/src/app/core/feedback/toast-host.scss
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.html
 M frontend/src/app/features/analytics/analytics-dashboard/analytics-dashboard.ts
 M frontend/src/app/features/conversations/conversation-list/conversation-list.html
 M frontend/src/app/features/conversations/conversation-list/conversation-list.scss
 M frontend/src/app/features/conversations/conversation-list/conversation-list.ts
 M frontend/src/app/features/customers/customer-detail/customer-detail.html
 M frontend/src/app/features/customers/customer-detail/customer-detail.scss
 M frontend/src/app/features/customers/customer-detail/customer-detail.ts
 M frontend/src/app/features/design-lab/design-lab.html
 M frontend/src/app/features/design-lab/design-lab.scss
 M frontend/src/app/features/design-lab/design-lab.ts
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.html
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.scss
 M frontend/src/app/features/notifications/notification-preferences/notification-preferences.ts
 M frontend/src/app/features/reports/report-schedules/report-schedules.html
 M frontend/src/app/features/reports/report-schedules/report-schedules.scss
 M frontend/src/app/features/reports/report-schedules/report-schedules.ts
 M frontend/src/app/features/reports/reports-page/reports-page.html
 M frontend/src/app/features/reports/reports-page/reports-page.scss
 M frontend/src/app/features/reports/reports-page/reports-page.ts
 M frontend/src/app/features/roles/permission-picker/permission-picker.ts
 M frontend/src/app/features/team/invite-member/invite-member.html
 M frontend/src/app/features/team/invite-member/invite-member.ts
 M frontend/src/app/features/team/member-detail/member-detail.html
 M frontend/src/app/features/team/member-detail/member-detail.ts
 M frontend/src/app/features/team/team-page/team-page.html
 M frontend/src/app/features/team/team-page/team-page.scss
 M frontend/src/app/features/team/team-page/team-page.ts
 M frontend/src/app/layout/command-palette/command-palette.scss
 M frontend/src/app/layout/entry-frame/entry-frame.html
 M frontend/src/app/layout/entry-frame/entry-frame.scss
 M frontend/src/app/layout/entry-frame/entry-frame.ts
 M frontend/src/app/layout/notification-bell/notification-bell.scss
 M frontend/src/app/shared/brand-mark/brand-mark.spec.ts
 M frontend/src/app/shared/brand-mark/brand-mark.ts
 M frontend/src/app/shared/voice/voice-chip.ts
 M frontend/src/app/shared/voice/voice.spec.ts
 M frontend/src/index.html
 M frontend/src/locale/messages.ar.json
 M frontend/src/locale/messages.json
 M frontend/src/styles/_admin.scss
 M frontend/src/styles/_primitives.scss
 M frontend/src/styles/_tokens.scss
?? docs/phase-reports/PHASE_39_2B_COMPONENTS_REPORT.md
?? frontend/src/app/shared/avatar/
?? frontend/src/app/shared/brand-mark/brand-geometry.ts
?? frontend/src/app/shared/brand-mark/brand-lockup.ts
?? frontend/src/app/shared/chip/
?? frontend/src/app/shared/choice/
?? frontend/src/app/shared/event-line/
?? frontend/src/app/shared/glass/
?? frontend/src/app/shared/orb/
?? frontend/src/app/shared/segmented/
?? frontend/src/app/shared/tooltip/
```

Nothing under `backend/` or `deploy/` changed. Nothing was committed.

### 3.1 The choice-control family (Phase 38 notes 3 and 4)

- **`shared/choice`**: `label[app-check]`, `label[app-radio]` and `label[app-switch]`. Each is an attribute component on the consumer's own `<label>`, and projects the consumer's own native `<input>` first, then its words. The input keeps its `formControlName`, `id`, `name`, `data-testid` and every e2e locator. Space, the arrow keys, the checked state and the `role="switch"` announcement all stay native. There is no ControlValueAccessor (plan decision 2).
- **The drawing** (`choice.scss`, unencapsulated, scoped to the family's classes):
  - a 20 px box (radius 6) with a drawn tick, a dash when mixed, and a 20 px round radio with a dot;
  - a 40×24 switch track with an 18 px thumb that travels to the end side and mirrors in RTL;
  - the label is `min-block-size: 44px` on every pointer, with the box centred on the first line;
  - checked is emerald, hover darkens the border, disabled is 50%;
  - in forced colours, `forced-color-adjust: none` with `ButtonText`, `Highlight` and `HighlightText`, so a checked box never depends on a colour the browser replaced.
- **`shared/segmented`**: `[app-segmented]` on the consumer's container, with `.segmented-option` options:
  - radios for a form choice (hidden input, whole segment is the label);
  - links with `aria-current` for an address (never `role="tab"`);
  - buttons with `aria-pressed` for a choice that acts at once.

  The track is the soft fill. The chosen segment is raised on the panel with a control-border edge and weight 600 (owner decision Q3). Segments are 36 px on a 44 px track with a fine pointer, and 44 px on touch.

- **Deleted from `_primitives.scss`**: the global `input[type='checkbox']:not(.switch), input[type='radio'], .form-check-input` rule, `label:has(> input…)`, `.switch` and every `.segmented*` rule. A checkbox outside the family now keeps the browser's own drawing (MASTER.md rule 11).
- **Every use moved** (all testids kept):

  | Where                        | Control                                             | Now                                                           |
  | ---------------------------- | --------------------------------------------------- | ------------------------------------------------------------- |
  | Team › invite, member detail | role checkboxes                                     | `label[app-check].role-option` tiles                          |
  | Roles (new, editor)          | permission checkboxes (`app-permission-picker`)     | `label[app-check].perm-option`                                |
  | Reports                      | catalogue radios; format and file-language segments | `label[app-radio].catalogue-item`; `[app-segmented]`          |
  | Report schedules             | frequency radios; format and language segments      | `label[app-radio].frequency-option`; `[app-segmented]`        |
  | Analytics                    | period segments                                     | `fieldset[app-segmented]`                                     |
  | Customer › merge             | candidate radios                                    | `label[app-radio].candidate`                                  |
  | Notification preferences     | five email switches, desktop alerts                 | `label[app-switch]`, the sentence under it as the description |
  | Inbox, team                  | view and invitation-list links                      | `nav[app-segmented]`                                          |
  | Entry pages                  | language buttons                                    | `div[app-segmented]`                                          |
  | Design lab                   | all of the above                                    | the family                                                    |

- **One behaviour kept by a fix:** notification preferences return focus to the first switch after saving. The code looked for `.category-switch`, which is now the label, so it focuses `.category-switch > input`. The existing spec caught this.

### 3.2 Components restyled in place, and new ones

- **Buttons** (`.btn*`): pills, 44 px (`.btn-sm` 40 px), `--text-button` 600.
  - Primary is emerald, secondary is a panel with a line, quiet is text only. `.btn-quiet` is new, and `.btn-ghost` is the same rule.
  - Icon-only buttons are round. A press is a 1 px settle that is 0 under reduced motion.
  - A busy button keeps its visually hidden label. This fixed the design lab's five unnamed buttons (axe `button-name`).
  - In forced colours, buttons keep a `ButtonText` border.
- **Inputs, selects, text areas**: 44 px, radius 12, the 3:1 control border. Focus is a 2 px emerald border (an inner line, so nothing moves) plus a 3 px halo, and an outline in forced colours.
- **Chips**:
  - `app-chip` (`shared/chip`) with tones need, team and waiting (gold), ai (emerald), failed (coral) and neutral, plus the status tones success, warning, danger and info with their glyphs.
  - The pill is 24 px (22 dense), with a 7 px dot and the word.
  - `.badge-*` are the same rules, so every status badge is a chip now.
  - `app-voice-chip` is a chip (a dot and the word, no glyph), and its colours cross over in 280 ms when the voice changes.
  - `.count-badge` gets a system colour pair in forced colours.
- **Panels and tiles**: `.panel-surface` (radius 24, a line, no shadow) and `.tile-surface` (radius 16, the soft fill). The bare names `.panel` and `.tile` are already used by component classes, so they were not taken.
- **Rows and avatars**:
  - `.list-row` with body, head, name, time, preview, chips and an `.unread-dot`. The selected row is the faint emerald tint; unread is the dot plus a heavier name.
  - `app-avatar` (`shared/avatar`) shows initials or a code, at 40, 34 or 64 px. The channel badge always carries its glyph: WhatsApp green, the website in emerald-600, other channels quiet.
  - An Arabic name shows one letter, because two Arabic initials join into a fragment of a word.
- **Bubbles**:
  - `.bubble-ai`, `.bubble-team` (`.bubble-human` is the same rule) and `.bubble-customer`, with no border and 18 px corners, 6 px on the speaker's side.
  - The team's gold edge is drawn inside, and each bubble rises 8 px and fades in.
  - The speaker inside each bubble uses the chip's text pair. axe caught emerald-300 at 4.42:1 on the dark AI bubble, which is fixed (section 4).
- **Event lines**: `app-event-line` (`role="note"`) and `.event-line`, with tones quiet, failed (coral), team and ai. `.handoff-divider` is the same rule.
- **The orb**: `app-orb` (`shared/orb`), 26 or 64 px, `aria-hidden`. It breathes (1 → `--orb-breath`) only while the AI answers, and is still under reduced motion.
- **Glass**:
  - `.menu`, `.popover` (new), `.tooltip` (new), the confirm dialog (on the scrim), the command palette, the notifications panel and toasts (radius 18) are glass, all through `@include glass`, which is still the only place that declares `backdrop-filter`.
  - The command palette and the notifications panel are layout components. Only their surface material changed; their layout waits for 39.3.
  - `shared/glass/glass-budget.ts` (`countBlurredLayers`, budget 3) counts what is on screen, and the design lab shows the count.
- **Tooltip**: `[appTooltip]` (`shared/tooltip`) is small glass, shown on hover and on focus.
  - It is hoverable and dismissed by Escape without moving focus (WCAG 1.4.13).
  - It is drawn inside the trigger and hidden from the accessibility tree, so it never changes the trigger's name. `describes` adds `aria-describedby` when it says more than the name.
  - Placement is `end` (logical) or `above`.
- **Skeletons**: the soft sheen, still under reduced motion, an outline in forced colours, and a new `.skeleton-avatar`. Each screen step draws its own geometry.
- **Empty states**: one Instrument Serif line (`--text-empty`; Plex Arabic 600 in Arabic), one sentence, one action, and a quiet glyph above in a soft circle (coral for the error tone).
- **Tokens added** (`_tokens.scss`):
  - `--emerald-775`, `--brand-ai`, `--brand-team` and `--brand-lens`, with their dark values;
  - `--avatar-bg` and `--avatar-fg`, `--channel-other`; `--channel-web` is now emerald-600 / emerald-300, as on the Inbox board;
  - `--chip-neutral-*`, `--chip-height-sm`, `--event-failed-line`;
  - `--choice-size`, `--choice-radius`, `--switch-inline`, `--switch-block` and `--switch-thumb`;
  - `--radius-toast`, the avatar and orb sizes, and `--text-button(-sm)` and `--text-empty` (with Arabic values).
- **Control heights**: `--control-height` 44, `--control-height-sm` 40, `--control-height-lg` 44, and 44 for all three on a coarse pointer.

### 3.3 The brand (plan 3.2.5)

- **`shared/brand-mark/brand-geometry.ts`** holds the mark: circles of radius 40 at (52, 60) and (88, 60) in a 140×120 box, and the lens path from `brand.md`.
- **The names are outlined paths.** "Nabra" comes from `public/fonts/geist/geist-latin.v1.woff2` instanced at weight 600, and «نبرة» from `ibm-plex-sans-arabic-arabic-700.v1.woff2`, shaped by HarfBuzz so the letters join. Both are normalised to 1,000 units per em. The tool was a throwaway Python venv in the evidence folder (fontTools + uharfbuzz); nothing was added to the project, and the venv was deleted.
- **`app-brand-mark`**:
  - the two circles and the lens, light emerald-700 / gold-500 / emerald-900 and dark emerald-100 / gold-500 / emerald-300;
  - `tone="mono"` and forced colours draw the outlines plus a filled lens in `currentColor`;
  - never mirrored, and the height follows the width at 6:7.
- **`app-brand-lockup`** (new): the mark, then "Nabra", then «نبرة», with the Arabic name first on Arabic pages. It is one image (`role="img"`, the product's name in the page's language).
- **Where it shows now:**
  - **The loading state** (`index.html` and `BootState`) uses the lockup. Before, it showed one name in the page's language; now it shows both, outlined, so no font is needed. `brand.spec.ts` now checks the outlined geometry and the Arabic-first rule instead of the text names.
  - **The entry pages' signature mark** is drawn with the new circles. The team circle travels in, then the lens fades in; under reduced motion nothing moves.
  - **The side bar, entry frame and hosted page** show the new mark through `app-brand-mark`. Their layouts (and whether they use the lockup) are 39.3, 39.8a and 39.5b.
  - **The favicons and app icons** are 39.8a, as planned; `nabra-icon.v1.*` still draws the Phase 36 shapes.

### 3.4 The design lab

The lab is rewritten for Calm Glass:

- **Header toggles:** theme, reading direction, and "Reduce transparency" (now a switch).
- **Foundations:** colour (live values), type, shape, space, panels and tiles, motion, and glass over the glows with a "Count the blurred layers" button.
- **The mark and three lockups:** English first, Arabic first, and one colour.
- **Every component in its states:**
  - buttons (five variants × default, dense, icon, disabled and busy, plus link and icon-only);
  - the choice family, including a long label with a hint, mixed, disabled and off;
  - segmented radios and links, inputs, voice chips and status chips, the take-over chip;
  - avatars at three sizes with each badge, and rows (selected, unread, failed);
  - the three voices with the orb and every event-line tone, the voice split and track;
  - menu, popover, tooltip, both dialogs, both toasts;
  - notices, skeletons, empty and error states with one action, a table and the pager;
  - icons, charts, and the plan and billing samples.

Every name, number and message on the lab is labelled sample content. The stylesheet stays under its 4 kB budget.

### 3.5 Documentation

- **`docs/design/MASTER.md`**:
  - status and implementation pointers;
  - §0 name and lockup (outlined), §2 direction and signature elements (Calm Glass);
  - §5 density (44/40 px), §6 radius and the new mark, §9 focus;
  - §10 rules 11 (the choice family; no global rule or `.switch`) and 12 (the glass declaration and the layer count);
  - five decision rows;
  - a new **§13.1** with the components' own 60 contrast pairs, and a new **§14** component catalogue with tokens.
- **`docs/architecture/admin-design-system.md`**: D1 (philosophy, now Calm Glass), D5 (radius, control sizing, glass users) and D16 (primitives and components), edited by hand with no Prettier pass.

### 3.6 Screens

| Screen                    | What changed                                                                                       | Components and tokens it now uses                                                 | Tests                                                                                                     |
| ------------------------- | -------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| Design lab                | rewritten (3.4)                                                                                    | every component; `--glass-*`, `--brand-*`, `--choice-*`, `--chip-*`, `--avatar-*` | Chrome: 16-cell full matrix with screenshots, modes, keyboard pass; `design-lab.ts` uses the shared specs |
| Notification preferences  | email and desktop switches; focus after save                                                       | `label[app-switch]`; `--switch-*`                                                 | `notification-preferences.spec.ts` (24 tests, focus fix); sound-test e2e                                  |
| Team › invite, member     | role checkboxes as tiles                                                                           | `label[app-check].role-option`; `--color-selected`, `--color-control-border`      | invite/member specs; invitation-language e2e                                                              |
| Roles (new, editor)       | permission checkboxes                                                                              | `label[app-check].perm-option`                                                    | role specs                                                                                                |
| Reports, report schedules | catalogue and frequency radios, format and language segments                                       | `label[app-radio]`, `[app-segmented]`                                             | report specs; arabic-report e2e (`label.segmented-option` kept)                                           |
| Analytics                 | period segments                                                                                    | `fieldset[app-segmented]`                                                         | analytics spec; time-zone e2e                                                                             |
| Customer                  | merge-candidate radios                                                                             | `label[app-radio].candidate`                                                      | customer-detail spec                                                                                      |
| Inbox, team               | view and list links                                                                                | `nav[app-segmented]`                                                              | conversation-list and team specs; needs-a-person e2e (`view-needs-person`)                                |
| Entry pages               | language buttons; the signature mark                                                               | `div[app-segmented]`; `BRAND_MARK`, `--brand-*`                                   | entry-frame spec; sign-in e2e                                                                             |
| Loading state             | the outlined lockup                                                                                | `app-brand-lockup`                                                                | `brand.spec.ts`, `brand-mark.spec.ts`                                                                     |
| All other screens         | buttons, inputs, chips, menus, toasts, dialogs, bubbles, skeletons, empty states restyled in place | `.btn*`, `.input`, `.chip`/`.badge`, glass, `.bubble-*`, `.event-line`            | whole Vitest suite; e2e 46/46 twice; 41-screen matrix                                                     |

New specs: `choice.spec.ts` (6), `segmented.spec.ts` (4), `chip.spec.ts` (5), `avatar.spec.ts` (8), `orb.spec.ts` (orb 2 + event line 1), `tooltip.spec.ts` (4), `glass-budget.spec.ts` (3). Rewritten: `brand-mark.spec.ts` (mark 4 + lockup 4). Updated: `voice.spec.ts` and `brand.spec.ts`.

**Screenshots** (`p39-2b\shots\`, 504 PNG):

- **After**, 172 files: all 41 screens at 1440 and 390 px in en-light and ar-dark, plus the widget closed and open on English and Arabic host pages (`<screen>-<width>-<lang>-<theme>-after.png`), taken on the final production build (design lab on the dev server).
- **Before**, 172 files with the same names (`…-before.png`). These are 39.2a's "after" captures. 39.2a was committed unchanged as `71f8e18`, which is this step's HEAD, so they show exactly the code this step started from. They were taken earlier on 2026-11-14 on the same data; times shown as "2 m ago" differ.
- **Design lab, full matrix**, 16 files: `design-lab-{390,768,1280,1920}-{en,ar}-{light,dark}-full-after.png`.
- **Modes**, 144 files: `{inbox,overview,conversation,hosted-chat,design-lab,notification-preferences,team-invite,reports,sign-in}-{1440,390}-{en-light,ar-dark}-{rt-media,rt-setting,reduced-motion,forced-colors}-after.png`. Each is only the first viewport.
- **Pairs to open first:**
  - `design-lab-1440-en-light-before/after`;
  - `notification-preferences-1440-en-light-before/after`;
  - `team-invite-1440-ar-dark-before/after`;
  - `reports-390-en-light-before/after`;
  - `inbox-1440-en-light-before/after`;
  - `overview-1440-ar-dark-before/after`;
  - `hosted-chat-390-ar-dark-before/after`;
  - `design-lab-390-ar-dark-full-after`.

## 4. Decisions

**Choices made:**

1. **Choice controls as attribute components on the consumer's `<label>`**, with unencapsulated styles scoped to the family. The plan named `app-check`, `app-radio`, `app-switch` and `app-segmented`; these are the same components with attribute selectors (`label[app-check]`, …). This keeps the consumer's element, so the e2e hooks (`label.segmented-option`, `role-option-*`, `permission-*`, `report-*`) and the test ids stay exactly where they were. Rejected: element wrappers, which would move the testid'd label inside a new element, and ControlValueAccessors (plan decision 2).
2. **A 44 px target on every pointer for the family's labels.** The specification says "a 44 px touch target". Applying it everywhere is simpler and safer than a coarse-only rule, because the label is the target. Segments are 36 px with a fine pointer (on a 44 px track) and 44 px on touch.
3. **Restyled in place rather than new markup** for buttons, inputs, badges, bubbles, the handoff divider, menus and toasts (plan decision 3). The new components (`app-chip`, `.list-row`, `app-avatar`, `app-event-line`, `app-orb`, `[appTooltip]`, `.popover`) exist for the screen steps. Moving each screen's markup onto them, and drawing its own skeleton geometry, is that screen's step.
4. **Status badges look like chips now** (pill, dot or glyph, word, no border). The owner's Q4 rule holds: the inbox and transcript only show voice chips.
5. **The voice chip uses a dot, not a glyph.** The specification's chips are a dot and a word; the word still says the voice.
6. **The lockup's names are outlined** (plan 3.2.2). The loading state shows both names in both languages, where before it showed one, as `brand.md` specifies the lockup. On Arabic pages the Arabic name leads. No font is needed for it.
7. **The tooltip shows on any focus**, not only on `:focus-visible`. A pointer already shows it by hovering, and the modality check made the behaviour order-dependent in the test environment. Fixed at the cause; never retried.
8. **Glass on the command palette and the notifications panel now** (plan table 3.2.3 lists them for 39.2b). Only the material changed; their layout and placement are 39.3.
9. **Two Two-Voices escape hatches stay until their screens move:** `.btn-ghost` = `.btn-quiet` and `.bubble-human` = `.bubble-team`. The alias layer still exists (39.8b deletes it).
10. **The design-lab "before" screenshots** are 39.2a's "after" set of the same code, rather than a second build of HEAD. That build was used for the startup-JS comparison.
11. **Chrome checks used Playwright** driving the installed Google Chrome with temporary contexts (about 1,000 scripted loads with axe, as in 39.2a). The Chrome DevTools MCP was used for the first live review of the lab. This is a departure in tool, not in what was checked.

**Fixes found by verification:**

- **The AI bubble's speaker in dark** was emerald-300 on `#1C4A40`, 4.42:1 (axe). The speaker now uses the chip's text pair (`--chip-ai-fg`, 8.38:1); the team's uses `--chip-need-fg` (8.14:1).
- **`.bell-badge` in forced colours**, which 39.2a left as an axe limitation (risk 4): it now has an explicit `Highlight`/`HighlightText` pair, and the forced-colours rows pass axe outright.
- **The command palette (52 B) and the bell (7 B)** went over their 4 kB warning budgets with the glass mixin. Both were trimmed (merged caption rules, Calm Glass names), not raised.

**Departures from the specification or the boards:**

| Where                                     | Specification / board                                   | Shipped                                                     | Why                                               |
| ----------------------------------------- | ------------------------------------------------------- | ----------------------------------------------------------- | ------------------------------------------------- |
| Segmented selected segment                | raised on panel with a soft shadow (board: `0 1px 3px`) | raised on panel, control-border edge, weight 600, no shadow | owner decision Q3; only glass casts a shadow      |
| Checkbox and input borders                | sand-400                                                | `--color-control-border`                                    | owner decision Q3 (3:1)                           |
| Avatar initials for Arabic names          | two initials (board: Latin "LH")                        | one letter                                                  | two Arabic letters join into a fragment of a word |
| Avatar channel badge                      | a coloured dot (board)                                  | the dot with the channel's glyph                            | colour is never the only signal                   |
| Voice chip                                | (Two Voices: glyph + word)                              | dot + word                                                  | calm-glass.md §6 chips                            |
| Bubble speaker inside the AI bubble, dark | AI label emerald-300 (board `aiLabel`)                  | `--chip-ai-fg` (emerald-100)                                | emerald-300 is 4.42:1 on the dark AI bubble       |
| Toast placement                           | bottom start (desktop), top (phone)                     | unchanged (bottom end)                                      | the plan gives placement to 39.3                  |

**Deferred, per the plan:**

- the side bar, phone bars and toast placement (39.3);
- each screen's rows, skeleton geometry and markup moves (39.4 to 39.8);
- favicons, app icons and the manifest (39.8a);
- the widget's glass header and composer (39.5b);
- deleting the alias layer (39.8b).

## 5. Verification

| Check                                    | Result                                                                                                                                                                                                                                                                                                                                                                                                                                                                                       |
| ---------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `npm run format:check`                   | **PASS**: all matched files use Prettier style (`gates\format-check.log`)                                                                                                                                                                                                                                                                                                                                                                                                                    |
| `npm run test:ci`                        | **PASS**: 132 files, 1,930 tests, 0 failures (39.2a: 125 / 1,893). Free memory before: 8.12 GB. One spec (tooltip) failed in the first full run because of an order-dependent modality check; the cause was fixed (decision 7), and the suite then passed in full twice, on the final code last                                                                                                                                                                                              |
| `npm run build`                          | **PASS**: 0 warnings, all budgets met. The palette and bell stylesheets were trimmed back under 4 kB, not raised. Initial 329.07 kB raw / 75.47 kB transfer (HEAD, built in the same session: 325.47 / 74.84). **Startup JS 144,368 B gzip** (446,646 raw, 127,418 brotli, 30 files). HEAD in the same session: 141,687 (+2,681 B, mostly the outlined lockup in the loading state). Against the 38.0 baseline of 138,907: **+3.9%**, under the 152,797 ceiling. Free memory before: 8.36 GB |
| `npm run i18n:check`                     | **PASS**: 2,471 messages, 0 errors, 0 warnings (39.2a: 2,454). 49 new, 32 retired (all design lab, plus `common.saveChanges`, which only the lab used)                                                                                                                                                                                                                                                                                                                                       |
| `npm run e2e`                            | **PASS, twice**: 46 of 46 (23 journeys × en-ltr and ar-rtl), 0 failures, no retries, 3.1 min each, both the **full suite** including the throttling journey. Run 1 ended 11:58; run 2 started at 13:03 and ran on the final code (more than 15 minutes apart). `NABRA_E2E_OUT` and `NABRA_E2E_STATE_DIR` were under `p39-2b\e2e\`, and the state folders were removed by the suite's teardown. Free memory before: 10.84 / 12.87 GB                                                          |
| Backend gates                            | **NOT RUN**: nothing under `backend/` changed in this step                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| Chrome: screenshots and checks           | **PASS**: 41 screens × {1440, 390} × {en-light, ar-dark} plus the widget on both host pages, 168 loads on the final production build. 0 overflow, one `h1` everywhere, 0 unnamed controls, 0 console errors, **axe 0 violations everywhere, including the design lab** (its 5 `button-name` from 39.0 are fixed)                                                                                                                                                                             |
| Chrome: full matrix                      | **PASS**: 41 screens × {390, 768, 1280, 1920} × {en, ar} × {light, dark} = 656 loads. 0 overflow, one `h1`, 0 unnamed, 0 console errors; axe at 1280: 0 violations                                                                                                                                                                                                                                                                                                                           |
| Design lab, full matrix with screenshots | **PASS**: 4 widths × 2 languages × 2 themes, 16 full-page loads with axe: 0 problems                                                                                                                                                                                                                                                                                                                                                                                                         |
| Reduced transparency                     | **PASS**: the media query and the in-app setting on 9 screens, 1440 and 390, en-light and ar-dark (72 loads). Glass resolves to the panel colour with no blur, and 0 elements blur. The hosted chat ignores the admin setting by design (visitor pages follow the device), and the media rows prove it there                                                                                                                                                                                 |
| Reduced motion                           | **PASS** (36 loads): `--duration-menu` 0.01 ms, travel 0; glass unchanged; 0 problems                                                                                                                                                                                                                                                                                                                                                                                                        |
| Forced colours                           | **PASS** (36 loads): glass is `Canvas` with no blur; axe 0 violations now that `.bell-badge` has its system pair                                                                                                                                                                                                                                                                                                                                                                             |
| Keyboard and focus                       | **PASS** in en and ar, fine pointer (1280) and touch (390): checkbox Space; radio and segmented arrows (mirrored in Arabic); switch Space with `role="switch"`; visible focus on the input and the segment; tooltip on focus with Escape keeping focus; dialog opening on the safe answer, Escape closing it and focus returning; menu Enter, Escape and focus return. On screen with a dialog open: **1 blurred layer** (budget 3). `chrome\keyboard.json`                                  |
| Touch targets                            | **PASS**: choice labels 44–47 px on every pointer (box 20 px); buttons and inputs 44 px; dense buttons 40 px with a fine pointer and 44 px on touch; segments 36 px with a fine pointer and 44 px on touch                                                                                                                                                                                                                                                                                   |
| Contrast of every new pair               | **PASS**: 60 of 60 (30 pairs × light and dark), the 39.2a composite method; tightest are the WhatsApp badge glyph at 3.22 (needs 3) and quiet time on the dark selected tint at 4.82 (needs 4.5). MASTER.md §13.1; `chrome\contrast-new.json`                                                                                                                                                                                                                                                |
| RTL mirroring                            | **PASS**: `dir` correct on every load; switch thumb, tooltip `end` placement and segment arrows mirror; the mark does not. `rtl_logical_lint.py` on the changed files: 0 findings except 2 pre-existing letter-spacing notes that `:lang(ar)` already zeroes                                                                                                                                                                                                                                 |
| Token, motion lints                      | `token_lint.py` on the changed files: 0 warnings, 1 info (the pre-existing "Loading…" fallback colour in `index.html`). The entry pages' deliberate local Arabic fallback font is now annotated. `motion_audit.py`: 0 findings. `lint\`                                                                                                                                                                                                                                                      |
| Fonts                                    | No font file changed in this step. First paint, production build, no cache: English sign-in fetches only `geist-latin.v1` (29,400 B, preloaded); Arabic sign-in fetches Geist plus Plex Arabic 400 (preloaded), 600 and 500, as in 39.2a. The loading state's lockup is outlined, so it needs no font, and an English page never fetches Arabic 700 for the logo. Swap layout shift was not re-measured (no font or fallback changed; 39.2a's numbers stand)                                 |
| Provider canary                          | **PASS** before any traffic and again at the end: every listed variable absent, `Email__Smtp__Host` loopback, `OMNIAI_DEV_AI_STANDIN` present (info). `gates\canary-*.log`                                                                                                                                                                                                                                                                                                                   |
| Secrets                                  | **PASS**: `scratch\secret-scan.mjs` scanned 1,150 files (this step's evidence, `frontend/src`, `frontend/e2e`, this report, MASTER.md and the design-system doc): 0 with the demo password, 0 with an invitation link. The cookie jar (`p39-2b\tmp\`) was deleted                                                                                                                                                                                                                            |
| Stack at the end                         | The API (`/health/ready` 200) and the dev server (:4200) are left running. The loopback production-build server (:4300) this step started is stopped. The HEAD build copy, its `node_modules` junction and the fontTools venv are removed. Nothing else was created or removed                                                                                                                                                                                                               |

## 6. See it live

- **The admin app:** http://localhost:4200/sign-in.
  - Accounts: the **Owner** `p36.owner@omniai.example`, the **Support agent** `p36.support@omniai.example` and the **Analyst** `p36.analyst@omniai.example` (passwords in `omniai-redesign-evidence\p36-01\demo-accounts.md`, not copied here).
  - The API (with the AI stand-in) and the dev server are left running.
- **The design lab:** http://localhost:4200/design-lab (development only, no sign-in).
  - Try the theme, direction and "Reduce transparency" toggles at the top.
  - Tab through **Choice controls** (Space, and the arrow keys on the radios and the 7/30/90-day segments).
  - Press **Count the blurred layers**, then open the menu, the popover or a dialog.
  - Focus the round "i" button for the tooltip and press Escape.
  - The **Mark** section has the three lockups.
  - Switch the app to Arabic in the account menu to see the rows, chips, bubbles and lockups right to left.
- **The forms whose controls moved:**
  - http://localhost:4200/notifications/preferences (the switches; save and see focus return to the first one);
  - http://localhost:4200/team/invite (role tiles);
  - http://localhost:4200/roles/new (permission checkboxes);
  - http://localhost:4200/reports (catalogue radios, format and language segments) and `/reports/schedules`;
  - http://localhost:4200/analytics (period segments);
  - a customer's page → "Merge" (candidate radios).
- **Buttons, chips and glass on every screen:** the inbox http://localhost:4200/conversations (view links, chips, the bell's glass panel), Ctrl K for the glass command palette, and the overview.
- **The loading lockup:** reload any page with a slow network in DevTools to see the mark, "Nabra" and «نبرة» before the app starts (Arabic first in Arabic).
- **The hosted chat:** http://localhost:4200/chat/9veAaKzu0Ee-isA7jrQzYw (`?lang=ar` for Arabic). Bubbles and chips are restyled; its layout is 39.5b.
- **Mailpit:** http://127.0.0.1:8026 (no email work in this step).

## 7. Backend asks

None new. Nothing in this step needs the API. The open asks are unchanged: BA39-2, BA39-3, BA39-4, BA-6 and BA-8 from the plan.

## 8. New Arabic for my review

All 49 new strings are in the development-only design lab: section notes and labelled sample content. Where a sample repeats a product phrase, it uses the product's accepted Arabic («بحاجة إلى الفريق», «استلام المحادثة: …», «بانتظار رد الفريق», «تعذّر على الذكاء الاصطناعي الرد», «غير مقروءة», «اليوم»).

| English                                                                                                                                                                                                                                                | Arabic                                                                                                                                                                                                                                                         | Where                                     |
| ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------- |
| Needs a person                                                                                                                                                                                                                                         | بحاجة إلى الفريق                                                                                                                                                                                                                                               | design lab (`lab.chips.need`)             |
| AI is answering                                                                                                                                                                                                                                        | الذكاء الاصطناعي يجيب                                                                                                                                                                                                                                          | design lab (`lab.chips.ai`)               |
| The AI could not reply                                                                                                                                                                                                                                 | تعذّر على الذكاء الاصطناعي الرد                                                                                                                                                                                                                                | design lab (`lab.chips.failed`)           |
| Handled by Sara                                                                                                                                                                                                                                        | استلام المحادثة: سارة                                                                                                                                                                                                                                          | design lab (`lab.chips.team`)             |
| Waiting for the team                                                                                                                                                                                                                                   | بانتظار رد الفريق                                                                                                                                                                                                                                              | design lab (`lab.chips.waiting`)          |
| Rows                                                                                                                                                                                                                                                   | الصفوف                                                                                                                                                                                                                                                         | design lab (`lab.rows.short`)             |
| A tile inside it: the soft fill.                                                                                                                                                                                                                       | بلاطة داخلها: التعبئة الناعمة.                                                                                                                                                                                                                                 | design lab (`lab.shape.tile`)             |
| Count the blurred layers                                                                                                                                                                                                                               | عُدّ الطبقات الضبابية                                                                                                                                                                                                                                          | design lab (`lab.glass.count`)            |
| Blurred layers on screen now: {n} (the budget is 3).                                                                                                                                                                                                   | الطبقات الضبابية على الشاشة الآن: {n} (والحد 3).                                                                                                                                                                                                               | design lab (`lab.glass.result`)           |
| Rows and avatars                                                                                                                                                                                                                                       | الصفوف والصور الرمزية                                                                                                                                                                                                                                          | design lab (`lab.rows`)                   |
| Radius 16, a 40px avatar with its channel's badge and glyph, the name and the time, one line of preview, then chips. Selected is a faint emerald tint; unread is a dot, a heavier name and the word "Unread" for screen readers.                       | زوايا بنصف قطر 16، وصورة رمزية بحجم 40 بكسل عليها شارة القناة ورمزها، ثم الاسم والوقت، وسطر واحد من المعاينة، ثم الشارات. المحدد بلون زمرّدي خفيف؛ وغير المقروء بنقطة واسم أثقل وكلمة «غير مقروءة» لقارئات الشاشة.                                             | design lab (`lab.rows.note`)              |
| Sample rows                                                                                                                                                                                                                                            | صفوف تجريبية                                                                                                                                                                                                                                                   | design lab (`lab.rows.list`)              |
| Unread                                                                                                                                                                                                                                                 | غير مقروءة                                                                                                                                                                                                                                                     | design lab (`lab.rows.unread`)            |
| The three voices: bubbles, the orb and event lines                                                                                                                                                                                                     | الأصوات الثلاثة: الفقاعات والكرة وخطوط الأحداث                                                                                                                                                                                                                 | design lab (`lab.transcript.voices`)      |
| "Calm Glass": the tokens and every component screens are built from, on one page. Development builds only. Every name, number and message here is sample content, written for this page.                                                               | «زجاج هادئ»: الرموز وكل المكوّنات التي تُبنى منها الشاشات، في صفحة واحدة. لنسخ التطوير فقط. كل اسم ورقم ورسالة هنا محتوى تجريبي كُتب لهذه الصفحة.                                                                                                              | design lab (`lab.intro.components`)       |
| Choices                                                                                                                                                                                                                                                | الاختيارات                                                                                                                                                                                                                                                     | design lab (`lab.choices.short`)          |
| Overlays                                                                                                                                                                                                                                               | العناصر الطافية                                                                                                                                                                                                                                                | design lab (`lab.overlays.short`)         |
| States                                                                                                                                                                                                                                                 | الحالات                                                                                                                                                                                                                                                        | design lab (`lab.states.short`)           |
| Panels and tiles                                                                                                                                                                                                                                       | اللوحات والبلاطات                                                                                                                                                                                                                                              | design lab (`lab.shape.surfaces`)         |
| Glass only where things float: menus, popovers, tooltips, dialogs, toasts and, from the next step, the side bar. At most three blurred layers on screen, and never on a list that scrolls. With "Reduce transparency" each one becomes its solid twin. | الزجاج فقط حيث تطفو العناصر: القوائم والنوافذ المنبثقة والتلميحات ومربعات الحوار والإشعارات، ومن الخطوة التالية الشريط الجانبي. ثلاث طبقات ضبابية على الشاشة على الأكثر، ولا ضبابية أبدًا على قائمة تُمرَّر. ومع «تقليل الشفافية» تصبح كلٌّ منها نظيرها الصلب. | design lab (`lab.glass.note`)             |
| Two circles, the AI and the team, and the one tone where they meet. Never mirrored, rotated or given a gradient. One colour: the outlines and the filled overlap.                                                                                      | دائرتان، الذكاء الاصطناعي والفريق، والنبرة الواحدة حيث تلتقيان. لا تُعكس ولا تُدار ولا تُعطى تدرّجًا لونيًا. وبلون واحد: الحدود الخارجية والتقاطع المملوء.                                                                                                     | design lab (`lab.mark.note.circles`)      |
| Pills, 44px tall (40px in dense headers), weight 600. One primary per area. Hover, press and focus are live: point at them, or press Tab.                                                                                                              | أزرار بيضوية بارتفاع 44 بكسل (40 في الرؤوس الكثيفة)، بوزن 600. زر أساسي واحد في كل منطقة. التمرير والضغط والتركيز تعمل فعلًا: أشِر إليها أو اضغط Tab.                                                                                                          | design lab (`lab.buttons.note.calmGlass`) |
| Dense                                                                                                                                                                                                                                                  | كثيف                                                                                                                                                                                                                                                           | design lab (`lab.buttons.dense`)          |
| Choice controls                                                                                                                                                                                                                                        | عناصر الاختيار                                                                                                                                                                                                                                                 | design lab (`lab.choices`)                |
| One family: a visible 20px box or a 40×24 track inside a 44px target, and the label is part of the target. Each wraps the native control, so keyboard and screen readers behave as they always do.                                                     | عائلة واحدة: مربع ظاهر بحجم 20 بكسل أو مسار بحجم 40×24 داخل هدف بحجم 44 بكسل، والتسمية جزء من الهدف. كلٌّ منها يغلّف عنصر التحكم الأصلي، فتعمل لوحة المفاتيح وقارئات الشاشة كما اعتادت.                                                                        | design lab (`lab.choices.note`)           |
| Checkboxes                                                                                                                                                                                                                                             | مربعات الاختيار                                                                                                                                                                                                                                                | design lab (`lab.choices.checkboxes`)     |
| Off                                                                                                                                                                                                                                                    | متوقف                                                                                                                                                                                                                                                          | design lab (`lab.choices.off`)            |
| Segmented controls                                                                                                                                                                                                                                     | أدوات التحكم المقسّمة                                                                                                                                                                                                                                          | design lab (`lab.choices.segmented`)      |
| Radios for a choice in a form, links when the choice is an address (never tabs for an address), buttons for a choice that acts at once.                                                                                                                | أزرار اختيار فردي لاختيار داخل نموذج، وروابط حين يكون الاختيار عنوانًا (ولا تبويبات للعناوين أبدًا)، وأزرار لاختيار يُنفَّذ فورًا.                                                                                                                             | design lab (`lab.choices.segmented.note`) |
| Some of them                                                                                                                                                                                                                                           | بعضها                                                                                                                                                                                                                                                          | design lab (`lab.choices.mixed`)          |
| Read conversations                                                                                                                                                                                                                                     | قراءة المحادثات                                                                                                                                                                                                                                                | design lab (`lab.choices.longLabel`)      |
| Open the inbox and read every message, without replying.                                                                                                                                                                                               | فتح صندوق المحادثات وقراءة كل رسالة، دون الرد.                                                                                                                                                                                                                 | design lab (`lab.choices.longHint`)       |
| Radios                                                                                                                                                                                                                                                 | أزرار الاختيار الفردي                                                                                                                                                                                                                                          | design lab (`lab.choices.radios`)         |
| Sample views                                                                                                                                                                                                                                           | طرق عرض تجريبية                                                                                                                                                                                                                                                | design lab (`lab.choices.views`)          |
| Needs a person                                                                                                                                                                                                                                         | بحاجة إلى الفريق                                                                                                                                                                                                                                               | design lab (`lab.choices.needsPerson`)    |
| Mine                                                                                                                                                                                                                                                   | محادثاتي                                                                                                                                                                                                                                                       | design lab (`lab.choices.mine`)           |
| Inputs, selects and text areas                                                                                                                                                                                                                         | حقول الإدخال والقوائم ومربعات النص                                                                                                                                                                                                                             | design lab (`lab.forms.inputs`)           |
| Open a popover                                                                                                                                                                                                                                         | فتح نافذة منبثقة                                                                                                                                                                                                                                               | design lab (`lab.popover.open`)           |
| Sample: a popover holds a little content, not a list of actions.                                                                                                                                                                                       | مثال: تحمل النافذة المنبثقة محتوى قليلًا، لا قائمة إجراءات.                                                                                                                                                                                                    | design lab (`lab.popover.body`)           |
| Sample: information                                                                                                                                                                                                                                    | مثال: معلومات                                                                                                                                                                                                                                                  | design lab (`lab.tooltip.label`)          |
| Sample: a tooltip names or explains.                                                                                                                                                                                                                   | مثال: التلميح يسمّي أو يشرح.                                                                                                                                                                                                                                   | design lab (`lab.tooltip.text`)           |
| Notices, skeletons, empty and error states                                                                                                                                                                                                             | التنبيهات والهياكل المؤقتة وحالات الفراغ والخطأ                                                                                                                                                                                                                | design lab (`lab.states`)                 |
| Connect a channel                                                                                                                                                                                                                                      | ربط قناة                                                                                                                                                                                                                                                       | design lab (`lab.empty.action`)           |
| Menus, popovers, tooltips, dialogs and toasts                                                                                                                                                                                                          | القوائم والنوافذ المنبثقة والتلميحات ومربعات الحوار والإشعارات                                                                                                                                                                                                 | design lab (`lab.overlays`)               |
| All glass. Focus moves in and returns to the control that opened it; Escape closes; a tooltip shows on hover and on keyboard focus.                                                                                                                    | كلها زجاج. ينتقل التركيز إليها ثم يعود إلى عنصر التحكم الذي فتحها؛ ومفتاح Escape يغلقها؛ ويظهر التلميح عند التمرير وعند التركيز بلوحة المفاتيح.                                                                                                                | design lab (`lab.overlays.note`)          |
| The AI is answering…                                                                                                                                                                                                                                   | الذكاء الاصطناعي يجيب…                                                                                                                                                                                                                                         | design lab (`lab.orb.answering`)          |
| Today                                                                                                                                                                                                                                                  | اليوم                                                                                                                                                                                                                                                          | design lab (`lab.event.today`)            |
| The AI could not reply · 10:41                                                                                                                                                                                                                         | تعذّر على الذكاء الاصطناعي الرد · 10:41                                                                                                                                                                                                                        | design lab (`lab.event.failed`)           |
| Switches                                                                                                                                                                                                                                               | المفاتيح                                                                                                                                                                                                                                                       | design lab (`lab.choices.switches`)       |

Retired message IDs (development-only lab text, plus `common.saveChanges`, which only the lab used): `lab.intro.calmGlass`, `common.saveChanges`, `lab.content`, `lab.feedback.short`, `lab.voices.short`, `lab.colour.sideBySide`, `lab.badge.active`, `lab.badge.paused`, `lab.shape.elevation`, `lab.mark.note`, `lab.buttons.note`, `lab.buttons.small`, `lab.buttons.large`, `lab.forms`, `lab.forms.radio`, `lab.content.card`, `lab.content.card.subtitle`, `lab.content.card.body`, `lab.content.badges`, `lab.content.menu`, `lab.content.dataList`, `lab.content.dataList.conversation`, `lab.content.dataList.handledBy`, `lab.content.dataList.lastActivity`, `lab.content.toolbar`, `lab.feedback`, `lab.voices`, `lab.voices.chips`, `lab.voices.sara`, `lab.voices.takeOver.note`, `lab.voices.track`, `lab.transcript`

## 9. Risks and open issues

1. **Taller controls everywhere.** Buttons and inputs went from 36 to 44 px (40 dense), and choice rows are 44 px. The 656-load matrix shows no overflow, but dense pages (tables with row actions, the reports catalogue, the role editor) are visibly taller, and some loading skeletons no longer match their loaded geometry exactly. Each screen step redraws its own (note 11).
2. **Two Two-Voices names stay as aliases** (`.btn-ghost`, `.bubble-human`, `.badge-*`, `.handoff-divider`), restyled to the Calm Glass look, until each screen's step moves its markup. 39.8b's grep gate should include them.
3. **The favicon and app icons still draw the Phase 36 shapes** until 39.8a. Until then the tab icon and the in-app mark differ.
4. **The loading state now shows both names** in every language, as the lockup does. If you prefer one name per language on the loading screen, the lockup takes a `lead`, and the second name could be hidden there.
5. **Toasts are glass but still at the bottom end.** 39.3 moves them (bottom start on desktop, top on phones).
6. **The tooltip is built but not used by a product screen yet.** 39.3's collapsed side bar is its first user, and 39.3 should check it is not clipped by a scrolling side bar (it is positioned inside its trigger).
7. **"Before" screenshots** are 39.2a's captures of the same code (section 4, decision 10), not re-shot today.

## 10. Proposed checkpoint commit message

```text
feat: give every checkbox, radio and switch a 44 px target with a visible
box, restyle buttons, inputs, chips, bubbles, menus, dialogs and toasts as
Calm Glass, draw the new two-circle mark and outlined lockup, and rewrite
the design lab (Phase 39.2b)

Build the choice-control family as attribute components on the consumer's
own label and native input (label[app-check], [app-radio], [app-switch],
[app-segmented]), delete the global checkbox rule and the .switch escape
class, and move every use with its ids and test ids; restyle .btn*, .input,
.badge (now chips), bubbles, the handoff line, skeletons and empty states in
place; make menus, popovers, tooltips, the confirm dialog, the command
palette, the notifications panel and toasts glass; add app-chip, app-avatar,
app-orb, app-event-line, the tooltip directive, rows and a blurred-layer
counter; draw the mark as two circles and the lockup's names as outlined
paths (the loading state uses it); fix the dark AI bubble's speaker contrast
and the bell badge in forced colours; rewrite the design lab and MASTER.md
(component catalogue, 60 more measured pairs) and the design-system doc.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```
