# Page brief: the entry pages (Phase 36.5)

Sign-in (`/sign-in`), registration (`/create-account`), an invitation (`/accept-invitation`) and
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

## Layout

| Width                | Layout                                                                                                                                                                                                                                                                                                                       |
| -------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1024 px and wider    | Two equal columns. Inline start: the lockup and the language switch on one row, then the page's content, centred, at most 25 rem wide, on `--surface-default`. Inline end: the story panel on `--surface-page` behind a hairline: the mark, the tagline (display size), the promise, and the example conversation in a card. |
| 1023 px and narrower | One column: a compact header (lockup at the start, language switch at the end), then the content. The story panel is not drawn.                                                                                                                                                                                              |

The form is first in the document at every width, so the keyboard and screen-reader order is the
task first; the panel follows. Arabic mirrors the whole frame with logical properties; the mark
does not mirror.

## The story panel

- Built only from the product's own parts: the mark, the transcript bubbles (`.bubble`), the
  voice words (`VOICE_WORDS`) and the handoff divider (`.handoff-divider`). No image and no
  script.
- The tagline and the promise are real text (an `aside` named by the tagline `h2`). The example is
  `aria-hidden`: it is decoration for a screen reader and adds nothing the promise has not said.
- The bubbles do not play their arrival animation here: it is a still picture.

## Exception to MASTER rules 7 and 10 (sample content)

MASTER rule 10 keeps sample content in the Design Lab, and rule 7 draws voice displays only from
real API data. The story panel shows an **example conversation** outside the Design Lab, because
the phase brief asks for "real UI fragments, not abstract art". It stays honest because:

1. It is labelled **"Example conversation" / «محادثة توضيحية»** on screen.
2. It has no names, dates, counts, ratings or anything a reader could take for their own data,
   and it shows only what the product does today: the AI answers from knowledge (a returns
   policy), a person takes over.
3. It appears only on the pages before the shell, never next to a workspace's data.

Rule 7 still governs every voice chip, split, track and divider inside the admin.

## Signature moment

The mark's Ember shape (a person) settles into the room the Lagoon shape (the AI) keeps for it:
translate only, from 26% / 18% of its own box to rest, `--duration-xl + --duration-sm` (600 ms)
on `--ease-emphasized` after `--duration-sm` (150 ms). Both shapes are fully drawn at every moment.
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

Inside the shell, as before. A calm state (never the error tone): what happened, that nothing in
the workspace changed, the path that matched nothing as an LTR island (without query or
fragment), and one primary way back to the overview. The placeholder is reached by no navigation
entry today; the development-only route `/design-lab-unbuilt-area` shows it with a sample name.
