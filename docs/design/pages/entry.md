# Page brief: the entry pages (Phase 36.5, Calm Glass in Phase 39.8a)

Sign-in (`/sign-in`, and its "session ended" state), registration (`/create-account`), forgot and
reset password (`/forgot-password`, `/reset-password`), an invitation (`/accept-invitation`) and
choosing or creating a workspace (`/workspace`): the pages a person meets before the admin shell.
They share one frame, `EntryFrame` (`frontend/src/app/layout/entry-frame/`).

## Five-second brief (Calm)

- **Who arrives:** workspace owners on a desktop; support agents and supervisors on a desktop or
  a phone; invited teammates from an invitation link, often without an account yet. English or
  Arabic, chosen by the browser.
- **Their job:** get into their workspace now.
- **Promise:** "The AI answers your customers. A person on your team steps in the moment one is
  needed." Tagline: "Two voices. One tone." / «صوتان… بنبرة واحدة».
- **One action per page:** sign in; create the account; sign in or create the account to join;
  choose or create the workspace.
- **Impression level:** Calm. No new JavaScript for the story, HTML and CSS in the page's lazy
  chunk; one enhancement of about 600 ms; the device floor is a low-end phone at 4x CPU.
- **LCP element:** the `h1`. Never animated, never hidden, in its final place in the first frame
  the page paints.

## Layout (Calm Glass, Phase 39.8a)

| Width                | Layout                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| -------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1024 px and wider    | A calm split on the ground (`--color-ground`, the shell's two glows), 14 px from the window's edges. Inline start: the lockup (mark, Nabra, «نبرة», the page's language first) and the language switch on one row, then the page on one solid panel (`--color-panel`, a `--color-line` edge, radius 24, no shadow), at most 26 rem of content, centred. Inline end, 12 px away: the brand side, a deep panel (`--entry-brand-*`: emerald-900; emerald-800 with a line in the dark look; the dark look's two glows inside it) with the mark at 120 px in its dark-ground colours, one serif line (the tagline, `--text-display`: Instrument Serif 44/48, Plex Arabic 600 in Arabic) and the promise. |
| 1023 px and narrower | One column: the lockup and the language switch, the page's panel, then the brand side as a short band after it (the mark at 56 px, the tagline at `--text-empty`, the promise).                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     |

The form is first in the document at every width, so the keyboard and screen-reader order is the
task first; the brand side follows on screen and in the document. Arabic mirrors the whole frame
with logical properties; the mark does not mirror. Nothing here floats, so nothing is glass: the
entry pages draw no blurred layer at all.

## The brand side

- Decoration only, so it is `aria-hidden` and holds nothing a reader needs: no heading, link or
  control. The page's own `h1`, words and actions are all in the form.
- Drawn with CSS and inline SVG only: the mark is three shapes, the glows are radial gradients.
  No image, no script, no font beyond the ones the page already uses (the serif line may swap;
  nothing is preloaded for it).
- Phase 36's example conversation is gone, and with it the exception to MASTER rules 7 and 10
  that it needed: one line and one sentence say what Nabra is.
- Forced colours: both panels become `Canvas` with a `CanvasText` edge, the glows are not drawn,
  and the mark is its one-colour form (outlines and the filled overlap).

## Signature moment

The team's circle (gold) settles beside the AI's (emerald), which is in place from the first
frame; only then does their overlap, the one tone, fade in: translate 28 px over
`--duration-dialog + --duration-press` (400 ms) on `--ease-calm` after `--duration-press`, then
the lens over `--duration-menu`.
It plays once per page load (`EntrySignature`), not on every move between sign-in and
registration, and not at all under `prefers-reduced-motion: reduce`. The mark is a logo, so its
travel never mirrors.

## Language switch

English / العربية before sign-in, each in its own name, `lang` and `dir`. It is a `.segmented`
group of buttons (`aria-pressed` on the current language; the pressed one has a 3:1
`--border-control` edge), 44 px on touch. A choice reloads the page (LanguageService). It is
withheld while an invitation token waits in memory (sign-in and registration during an invitation,
and the invitation page until the token is spent), because the reload would lose it.

## Not found and the unbuilt-area placeholder

Inside the shell, as before (so not the split). A calm state (never the error tone), under
the mark at 56 px and one serif line (Phase 39.8a): what happened, that nothing in
the workspace changed, the path that matched nothing as an LTR island (without query or
fragment), and one primary way back to the overview. The placeholder is reached by no navigation
entry today; the development-only route `/design-lab-unbuilt-area` shows it with a sample name.
