import { IconName } from '@app/shared/icon/icon-paths';

/** The palette's sections, in the order they are shown. */
export const COMMAND_GROUPS = ['navigate', 'actions', 'preferences'] as const;

export type CommandGroup = (typeof COMMAND_GROUPS)[number];

/**
 * One thing the palette can do: open a page, or perform an action.
 *
 * Commands, never data. The palette searches what the product can do for the
 * member in front of it -- built from the navigation model and their
 * permissions -- and nothing the workspace holds.
 */
export interface Command {
  /** Stable, language-independent; also the base of the option's element id. */
  readonly id: string;
  readonly group: CommandGroup;
  readonly label: string;
  /** Other words somebody might type for it, in the page's language. */
  readonly keywords?: readonly string[];
  readonly icon: IconName;
  /** A short state beside the label: "Current page", "In use". */
  readonly hint?: string;
  /** Set for a label written in another language (the language switch). */
  readonly lang?: string;
  readonly dir?: 'ltr' | 'rtl';
  readonly run: () => void;
}

/**
 * Text folded for matching: case, Latin accents and Arabic diacritics removed,
 * and the Arabic letters people type interchangeably made one.
 *
 * - Hamza forms of alef (أ إ آ ٱ) become ا; ة becomes ه; ى becomes ي; ؤ becomes
 *   و and ئ becomes ي -- a reader typing "الاعدادات" must find "الإعدادات".
 * - Tashkeel and every other combining mark go (NFKD, then the `M` category),
 *   which also folds "é" to "e".
 * - Tatweel (ـ) is decoration and goes too.
 */
export function foldForMatch(text: string): string {
  return text
    .normalize('NFKD')
    .replace(/\p{M}/gu, '')
    .replace(/[أإآٱ]/g, 'ا')
    .replace(/ة/g, 'ه')
    .replace(/ى/g, 'ي')
    .replace(/ؤ/g, 'و')
    .replace(/ئ/g, 'ي')
    .replace(/ـ/g, '')
    .toLowerCase()
    .replace(/\s+/g, ' ')
    .trim();
}

/**
 * How well a command matches a query; 0 means it does not.
 *
 * Every word of the query has to be found. A word that starts a word of the
 * label counts most, one found inside the label next, and one found only among
 * the keywords least -- so typing "inv" puts "Invite a member" above a page
 * that merely lists invitations among its keywords.
 */
export function matchScore(command: Command, query: string): number {
  const words = foldForMatch(query).split(' ').filter(Boolean);

  if (words.length === 0) {
    return 1;
  }

  const label = foldForMatch(command.label);
  const labelWords = label.split(' ');
  const keywords = (command.keywords ?? []).map(foldForMatch).join(' ');
  let score = 0;

  for (const word of words) {
    if (labelWords.some((part) => part.startsWith(word))) {
      score += 3;
    } else if (label.includes(word)) {
      score += 2;
    } else if (keywords.includes(word)) {
      score += 1;
    } else {
      return 0;
    }
  }

  return score;
}

/** The commands that match, best first; equal matches keep their given order. */
export function rankCommands(commands: readonly Command[], query: string): readonly Command[] {
  return commands
    .map((command, order) => ({ command, order, score: matchScore(command, query) }))
    .filter((entry) => entry.score > 0)
    .sort((a, b) => b.score - a.score || a.order - b.order)
    .map((entry) => entry.command);
}

const ARABIC_SCRIPT = /\p{Script=Arabic}/u;

/** A label cut around the part the reader typed, for emphasis. */
export interface LabelParts {
  readonly before: string;
  readonly match: string;
  readonly after: string;
}

/**
 * Splits a label around the first place the query appears in it, ignoring case.
 *
 * Exact text only: a match found through folding (an Arabic diacritic, an
 * accent) is shown without emphasis rather than with an emphasis drawn in the
 * wrong place. Rendered as text, never as markup.
 *
 * Never inside Arabic. Arabic letters join, and a change of weight in the middle
 * of a word breaks the join -- "التحليلات" emphasised as "الت" + "حليلات" is
 * seen in Chrome as two broken words. The list narrowing is the feedback there.
 */
export function labelParts(label: string, query: string): LabelParts {
  const needle = query.trim().toLowerCase();
  const at = needle && !ARABIC_SCRIPT.test(label) ? label.toLowerCase().indexOf(needle) : -1;

  if (at < 0) {
    return { before: label, match: '', after: '' };
  }

  return {
    before: label.slice(0, at),
    match: label.slice(at, at + needle.length),
    after: label.slice(at + needle.length),
  };
}
