import { Injectable, inject } from '@angular/core';
import { APP_LANGUAGE, AppLanguage } from './language';

/**
 * Every date, time, number, currency and percentage the product shows.
 *
 * One module, so the policy lives in one place (localisation-arabic-rtl.md, 3.3
 * and 5.4):
 *
 * - **Western digits in both languages**, always: `numberingSystem: 'latn'`.
 *   Chromium's Arabic defaults differ by region (`ar` and `ar-AE` give 0-9,
 *   `ar-SA` and `ar-EG` give Arabic-Indic digits), so nothing is left to them.
 * - **The Gregorian calendar**, always: `calendar: 'gregory'`.
 * - **Plain `en` and `ar` tags**, never a region, for the same reason.
 * - **Explicit fields, never `dateStyle`**: Arabic `dateStyle: 'medium'` is
 *   all-numeric (18/10/2026), which reads as a code, not a date.
 *
 * The time zone stays the browser's unless a caller passes one (the API's
 * date-only values are UTC and say so).
 */

/** The named shapes a date can take. Each reads naturally in both languages. */
export type DateStyle =
  /** Oct 18, 2026 · 18 أكتوبر 2026 */
  | 'date'
  /** October 18, 2026 · 18 أكتوبر 2026 */
  | 'longDate'
  /** Oct 18, 2026, 2:05 PM · 18 أكتوبر 2026 في 2:05 م */
  | 'dateTime'
  /** Oct 18 · 18 أكتوبر */
  | 'dayMonth'
  /** Oct 18, 2:05 PM · 18 أكتوبر، 2:05 م */
  | 'dayMonthTime'
  /** October 2026 · أكتوبر 2026 */
  | 'monthYear'
  /** 2:05 PM · 2:05 م */
  | 'time';

const DATE_STYLES: Readonly<Record<DateStyle, Intl.DateTimeFormatOptions>> = {
  date: { day: 'numeric', month: 'short', year: 'numeric' },
  longDate: { day: 'numeric', month: 'long', year: 'numeric' },
  dateTime: { day: 'numeric', month: 'short', year: 'numeric', hour: 'numeric', minute: '2-digit' },
  dayMonth: { day: 'numeric', month: 'short' },
  dayMonthTime: { day: 'numeric', month: 'short', hour: 'numeric', minute: '2-digit' },
  monthYear: { month: 'long', year: 'numeric' },
  time: { hour: 'numeric', minute: '2-digit' },
};

/** What every Arabic and English formatter is given, whatever the caller asks. */
const POLICY = { numberingSystem: 'latn' } as const;
const DATE_POLICY = { ...POLICY, calendar: 'gregory' } as const;

const cache = new Map<string, Intl.DateTimeFormat | Intl.NumberFormat | Intl.RelativeTimeFormat>();

function cached<T extends Intl.DateTimeFormat | Intl.NumberFormat | Intl.RelativeTimeFormat>(
  key: string,
  create: () => T,
): T {
  let formatter = cache.get(key) as T | undefined;

  if (!formatter) {
    formatter = create();
    cache.set(key, formatter);
  }

  return formatter;
}

function toDate(value: string | number | Date): Date {
  return value instanceof Date ? value : new Date(value);
}

export function formatDate(
  language: AppLanguage,
  value: string | number | Date,
  style: DateStyle = 'date',
  timeZone?: string,
): string {
  const date = toDate(value);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const formatter = cached(
    `d|${language}|${style}|${timeZone ?? ''}`,
    () =>
      new Intl.DateTimeFormat(language, {
        ...DATE_STYLES[style],
        ...DATE_POLICY,
        ...(timeZone ? { timeZone } : {}),
      }),
  );

  return formatter.format(date);
}

export function formatNumber(
  language: AppLanguage,
  value: number,
  options: Intl.NumberFormatOptions = {},
): string {
  const formatter = cached(
    `n|${language}|${JSON.stringify(options)}`,
    () => new Intl.NumberFormat(language, { ...options, ...POLICY }),
  );

  return formatter.format(value);
}

/** A ratio (0.42) as a percentage (42%). */
export function formatPercent(
  language: AppLanguage,
  ratio: number,
  maximumFractionDigits = 0,
): string {
  return formatNumber(language, ratio, { style: 'percent', maximumFractionDigits });
}

/**
 * An amount in a currency the API named. The currency always comes from the
 * data: Angular's Arabic locale data defaults to EGP, so nothing here assumes one.
 */
export function formatCurrency(
  language: AppLanguage,
  amount: number,
  currency: string,
  options: Pick<Intl.NumberFormatOptions, 'minimumFractionDigits' | 'maximumFractionDigits'> = {},
): string {
  return formatNumber(language, amount, { ...options, style: 'currency', currency });
}

/** The currency's own number of decimal places (2 for USD, 0 for JPY, 3 for KWD). */
export function currencyExponent(currency: string): number {
  return (
    new Intl.NumberFormat('en', { style: 'currency', currency }).resolvedOptions()
      .maximumFractionDigits ?? 2
  );
}

/** "3 minutes ago" · "قبل 3 دقائق". */
export function formatRelative(
  language: AppLanguage,
  value: number,
  unit: Intl.RelativeTimeFormatUnit,
): string {
  const formatter = cached(
    `r|${language}`,
    () => new Intl.RelativeTimeFormat(language, { numeric: 'auto', ...POLICY }),
  );

  return formatter.format(value, unit);
}

/**
 * Formatting for components and pipes, in the language this page runs in.
 *
 * The language is fixed for a page's lifetime (a switch reloads), so the methods
 * are plain functions of their arguments and the pipes on top of them are pure.
 */
@Injectable({ providedIn: 'root' })
export class LocaleFormat {
  readonly language = inject(APP_LANGUAGE);

  date(value: string | number | Date, style: DateStyle = 'date', timeZone?: string): string {
    return formatDate(this.language, value, style, timeZone);
  }

  number(value: number, options?: Intl.NumberFormatOptions): string {
    return formatNumber(this.language, value, options);
  }

  percent(ratio: number, maximumFractionDigits?: number): string {
    return formatPercent(this.language, ratio, maximumFractionDigits);
  }

  currency(
    amount: number,
    currency: string,
    options?: Pick<Intl.NumberFormatOptions, 'minimumFractionDigits' | 'maximumFractionDigits'>,
  ): string {
    return formatCurrency(this.language, amount, currency, options);
  }

  relative(value: number, unit: Intl.RelativeTimeFormatUnit): string {
    return formatRelative(this.language, value, unit);
  }
}
