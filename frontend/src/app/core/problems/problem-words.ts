import {
  EnvironmentProviders,
  makeEnvironmentProviders,
  provideEnvironmentInitializer,
} from '@angular/core';
import type { LimitUnit } from '@app/core/api/api-error';
import { AppLanguage } from '@app/core/i18n/language';
import { CORE_PROBLEM_WORDS } from './core-problems';

/** What a refusal said beyond its code, for the wordings that need it. */
export interface ProblemFacts {
  /** The language the page is in, for words that change with a count. */
  readonly language: AppLanguage;

  /** From `Retry-After`, in seconds, when the server sent a usable one. */
  readonly retryAfterSeconds?: number;

  /** The bound a refused value broke, in the rule's own unit. */
  readonly limit?: number;

  /** What `limit` counts, when the server says (BA-12). */
  readonly limitUnit?: LimitUnit;

  /** Every rule the server named, the first one included. */
  readonly codes: readonly string[];
}

/** A sentence, or the way to build one from the refusal's facts. */
export type ProblemWording = string | ((facts: ProblemFacts) => string);

/** One area's refusals, by the server's stable `code`. */
export type ProblemCatalogue = Readonly<Record<string, ProblemWording>>;

/**
 * The one place a refusal's `code` becomes words.
 *
 * Starts with the account and generic codes, which the sign-in page needs before
 * any feature has loaded. Everything else arrives with the feature that can meet
 * it (`provideProblemWords`), so a hundred-odd sentences never sit in the startup
 * JavaScript.
 *
 * A plain map rather than a service, because `toApiError` is a plain function
 * called from everywhere, and giving it an injector would change every caller.
 */
const registry = new Map<string, ProblemWording>(Object.entries(CORE_PROBLEM_WORDS));

/** Adds an area's refusals to the registry. Registering twice changes nothing. */
export function registerProblemWords(catalogue: ProblemCatalogue): void {
  for (const [code, wording] of Object.entries(catalogue)) {
    registry.set(code, wording);
  }
}

/** The words for a code, or undefined for a code nobody has registered. */
export function problemWording(code: string, facts: ProblemFacts): string | undefined {
  const wording = registry.get(code);

  return typeof wording === 'function' ? wording(facts) : wording;
}

/** Whether a catalogue has registered words for this code. */
export function hasProblemWording(code: string): boolean {
  return registry.has(code);
}

/**
 * Registers an area's refusals when its routes load.
 *
 * For the `providers` of a lazy route: the words then arrive with the feature
 * and cost the startup JavaScript nothing.
 */
export function provideProblemWords(catalogue: ProblemCatalogue): EnvironmentProviders {
  return makeEnvironmentProviders([
    provideEnvironmentInitializer(() => registerProblemWords(catalogue)),
  ]);
}
