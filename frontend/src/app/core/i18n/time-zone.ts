import type { AppLanguage } from './language';
import { formatDate, type DateStyle } from './locale-format';

/**
 * The workspace's time zone, in words and in dates (Phase 38.5, E2).
 *
 * Every day boundary in analytics, reports and schedules is a local day of the
 * workspace's zone (`UTC` unless somebody chose another; Phase 37.6), and every
 * response says which zone it used. These helpers are the one place a screen
 * turns that zone into a label, an instant into a time of day there, or "now"
 * into the zone's today.
 *
 * Kept apart from `locale-format.ts` so its sentences arrive with the screens
 * that use them, never in the startup JavaScript.
 *
 * <strong>Zones are named by their IANA id</strong>, as the report files name
 * them (37.7): `Asia/Riyadh`. The workspace settings add the zone's localised
 * name beside the id where the browser offers one (`zoneName`, 39.6b), never in
 * its place. The id is wrapped in a left-to-right isolate, so it keeps its
 * order inside an Arabic sentence.
 */

/** The zone every workspace has until somebody chooses another. */
export const DEFAULT_TIME_ZONE = 'UTC';

const LRI = '⁦';
const PDI = '⁩';

/** An IANA id, isolated so the slash and the Latin letters keep their order in Arabic. */
export function isolatedZone(zone: string): string {
  return `${LRI}${zone}${PDI}`;
}

/** Whether this browser can format dates in the zone. */
export function isKnownZone(zone: string): boolean {
  try {
    new Intl.DateTimeFormat('en', { timeZone: zone });

    return true;
  } catch {
    return false;
  }
}

/**
 * The zone to format instants in: the one asked for, or `UTC` when this
 * browser does not know it.
 *
 * The server accepts only names its own runtime knows, so in practice this is
 * always the zone itself. The fallback exists so a browser with older zone data
 * shows a correct instant labelled `UTC` rather than throwing and blanking the
 * page; callers label an instant with the zone this returns, never with the one
 * they asked for.
 */
export function displayZone(zone: string): string {
  return isKnownZone(zone) ? zone : DEFAULT_TIME_ZONE;
}

/** Whether the zone is the universal one, which has its own words in Arabic. */
function isUtc(zone: string): boolean {
  return zone === 'UTC' || zone === 'Etc/UTC';
}

/**
 * The zone inside a sentence: "in UTC" / «بالتوقيت العالمي», or "in Asia/Riyadh
 * time" / «بتوقيت Asia/Riyadh».
 */
export function zonePhrase(zone: string): string {
  if (isUtc(zone)) {
    return $localize`:@@zone.phrase.utc:in UTC`;
  }

  const name = isolatedZone(zone);

  return $localize`:@@zone.phrase.named:in ${name}:zone: time`;
}

/**
 * The zone as a short label, for a column heading or beside a time: "UTC" /
 * «بالتوقيت العالمي», or "Asia/Riyadh time" / «بتوقيت Asia/Riyadh».
 */
export function zoneLabel(zone: string): string {
  if (isUtc(zone)) {
    return $localize`:@@zone.label.utc:UTC`;
  }

  const name = isolatedZone(zone);

  return $localize`:@@zone.label.named:${name}:zone: time`;
}

/** An instant, shown as it reads in the zone. */
export function formatInZone(
  language: AppLanguage,
  value: string | number | Date,
  style: DateStyle,
  zone: string,
): string {
  return formatDate(language, value, style, displayZone(zone));
}

const partsCache = new Map<string, Intl.DateTimeFormat>();

/** The year, month, day, hour, minute and second of an instant in a zone. */
function partsIn(instant: Date, zone: string): Readonly<Record<string, number>> {
  const resolved = displayZone(zone);
  let formatter = partsCache.get(resolved);

  if (!formatter) {
    formatter = new Intl.DateTimeFormat('en-US', {
      timeZone: resolved,
      calendar: 'gregory',
      numberingSystem: 'latn',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hourCycle: 'h23',
    });
    partsCache.set(resolved, formatter);
  }

  const parts: Record<string, number> = {};

  for (const part of formatter.formatToParts(instant)) {
    if (part.type !== 'literal') {
      parts[part.type] = Number(part.value);
    }
  }

  return parts;
}

/** The local date (`yyyy-MM-dd`) an instant falls on in a zone. */
export function localDate(instant: string | number | Date, zone: string): string {
  const date = instant instanceof Date ? instant : new Date(instant);

  if (Number.isNaN(date.getTime())) {
    return '';
  }

  const { year, month, day } = partsIn(date, zone);

  return `${String(year).padStart(4, '0')}-${pad(month)}-${pad(day)}`;
}

/** Whether an instant is exactly the start of a local day in a zone. */
export function isLocalMidnight(instant: string | number | Date, zone: string): boolean {
  const date = instant instanceof Date ? instant : new Date(instant);

  if (Number.isNaN(date.getTime())) {
    return false;
  }

  const { hour, minute, second } = partsIn(date, zone);

  return hour === 0 && minute === 0 && second === 0 && date.getUTCMilliseconds() === 0;
}

/** Today's date (`yyyy-MM-dd`) in a zone: the "today" a custom report range is judged by. */
export function todayIn(zone: string, now: Date = new Date()): string {
  return localDate(now, zone);
}

/**
 * A local date (`yyyy-MM-dd`) in words.
 *
 * A date the API sent as a local date of the workspace's zone is already the
 * right day: it is formatted from its parts (pinned to UTC so the browser's own
 * zone cannot move it), never re-read as an instant.
 */
export function formatLocalDate(
  language: AppLanguage,
  date: string,
  style: 'dayMonth' | 'longDate' | 'date' = 'date',
): string {
  const [year, month, day] = date.split('-').map(Number);

  if (!year || !month || !day) {
    return date;
  }

  return formatDate(language, new Date(Date.UTC(year, month - 1, day)), style, 'UTC');
}

/** The day after a local date, as `yyyy-MM-dd`. Calendar arithmetic, so no zone is involved. */
export function nextLocalDate(date: string): string {
  const next = new Date(`${date}T00:00:00Z`);
  next.setUTCDate(next.getUTCDate() + 1);

  return next.toISOString().slice(0, 10);
}

/**
 * The zone's name in the reader's language, where this browser offers one
 * (Phase 38 note 8, Phase 39.6b): "Arabian Standard Time" /
 * «التوقيت العربي الرسمي» for `Asia/Riyadh`, "Coordinated Universal Time" /
 * «التوقيت العالمي المنسق» for `UTC`.
 *
 * The generic name first ("Eastern Time"), then the long one where the
 * generic is only an offset (UTC). A name that is an offset ("GMT+03:00"), the
 * id itself, or a zone this browser does not know gives null: the screen then
 * shows the id alone. The id always stays visible beside the name, because it
 * is what the server stores and what the report files print.
 */
export function zoneName(
  zone: string,
  language: AppLanguage,
  at: Date = new Date(),
): string | null {
  for (const style of ['longGeneric', 'long'] as const) {
    try {
      const name = new Intl.DateTimeFormat(language, {
        timeZone: zone,
        timeZoneName: style,
        numberingSystem: 'latn',
      })
        .formatToParts(at)
        .find((part) => part.type === 'timeZoneName')?.value;

      if (name && !/\d/.test(name) && name !== zone) {
        return name;
      }
    } catch {
      return null;
    }
  }

  return null;
}

/** The browser's own zone, offered as a suggestion; null if the browser does not say. */
export function browserZone(): string | null {
  try {
    const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;

    return typeof zone === 'string' && zone.length > 0 ? zone : null;
  } catch {
    return null;
  }
}

/**
 * Every zone this browser knows, from `Intl`: no list is shipped.
 *
 * The server judges a name (`workspace.time_zone.invalid`), and its list and the
 * browser's differ at the edges (aliases, very new zones), so a name missing
 * here can still be saved and a name present here can still be refused.
 */
export function supportedZones(): readonly string[] {
  const intl = Intl as typeof Intl & { supportedValuesOf?: (key: 'timeZone') => string[] };

  try {
    return intl.supportedValuesOf?.('timeZone') ?? [];
  } catch {
    return [];
  }
}

function pad(value: number): string {
  return String(value).padStart(2, '0');
}
