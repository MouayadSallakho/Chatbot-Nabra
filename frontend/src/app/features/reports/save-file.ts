/**
 * Saves a file the page has already read, under the given name.
 *
 * An object URL on a temporary link with `download`, clicked and then let go:
 * no markup is written, nothing is opened in a tab, and the URL is revoked once
 * the browser has taken the file.
 */
export function saveFile(document: Document, blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');

  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.hidden = true;
  document.body.append(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), REVOKE_AFTER_MS);
}

/** Long enough for the browser to start the download from the URL. */
const REVOKE_AFTER_MS = 10_000;

/** A name for a file the server sent without one: the report's period and its format. */
export function fallbackFileName(periodFrom: string, format: string): string {
  const extension = format.toLowerCase();

  return `report-${periodFrom}.${extension}`;
}
