import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { toApiError } from '@app/core/api/api-error';
import { registerProblemWords } from '@app/core/problems/problem-words';
import { REPORT_PROBLEM_WORDS } from './report-problems';
import { fileNameFrom, problemFromBlob } from './reports-api';
import { fallbackFileName, saveFile } from './save-file';

/** The client download's parts (Phase 39.7a, note 16). */
describe('report downloads', () => {
  describe('fileNameFrom', () => {
    it('prefers the UTF-8 name, so an Arabic file keeps its Arabic name', () => {
      expect(
        fileNameFrom(
          "attachment; filename=report.csv; filename*=UTF-8''%D8%AA%D9%82%D8%B1%D9%8A%D8%B1.csv",
        ),
      ).toBe('تقرير.csv');
    });

    it('reads a plain or quoted name when there is no UTF-8 one', () => {
      expect(fileNameFrom('attachment; filename="conversation-activity.pdf"')).toBe(
        'conversation-activity.pdf',
      );
      expect(fileNameFrom('attachment; filename=usage.xlsx')).toBe('usage.xlsx');
    });

    it('never lets a header name a folder or carry control characters', () => {
      expect(fileNameFrom('attachment; filename="../../etc/passwd"')).toBe('passwd');
      expect(fileNameFrom("attachment; filename*=UTF-8''..%5Cwin%5Cr%0Aeport.csv")).toBe(
        'report.csv',
      );
    });

    it('gives nothing when there is no name', () => {
      expect(fileNameFrom(null)).toBeNull();
      expect(fileNameFrom('attachment')).toBeNull();
      expect(fileNameFrom('attachment; filename=""')).toBeNull();
    });
  });

  describe('problemFromBlob', () => {
    it("reads a refusal's problem out of its blob, so it is worded by its code", async () => {
      registerProblemWords(REPORT_PROBLEM_WORDS);

      const failure = new HttpErrorResponse({
        error: new Blob([JSON.stringify({ status: 409, code: 'reports.file.not_ready' })]),
        headers: new HttpHeaders({ 'Retry-After': '5' }),
        status: 409,
        url: '/api/x/download',
      });
      const error = toApiError(await problemFromBlob(failure));

      expect(error.code).toBe('reports.file.not_ready');
      expect(error.kind).toBe('conflict');
      expect(error.message).toBe(
        'This report is still being prepared. Try the download again in a moment.',
      );
    });

    it('leaves anything that is not a JSON blob as it was', async () => {
      const text = new HttpErrorResponse({ error: new Blob(['<html>']), status: 502 });
      const plain = new HttpErrorResponse({ error: { code: 'x' }, status: 400 });

      expect(await problemFromBlob(text)).toBe(text);
      expect(await problemFromBlob(plain)).toBe(plain);
      expect(await problemFromBlob('down')).toBe('down');
    });
  });

  describe('saveFile', () => {
    it('clicks a hidden download link for an object URL, removes it, and lets the URL go', () => {
      vi.useFakeTimers();

      const create = vi.fn(() => 'blob:file');
      const revoke = vi.fn();
      const original = { create: URL.createObjectURL, revoke: URL.revokeObjectURL };
      const clicked: { download: string; hidden: boolean; connected: boolean }[] = [];

      URL.createObjectURL = create;
      URL.revokeObjectURL = revoke;
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (
        this: HTMLAnchorElement,
      ) {
        clicked.push({
          download: this.download,
          hidden: this.hidden === true,
          connected: this.isConnected,
        });
      });

      try {
        saveFile(document, new Blob(['x']), 'report.csv');

        expect(clicked).toEqual([{ download: 'report.csv', hidden: true, connected: true }]);
        expect(document.querySelector('a[download]')).toBeNull();
        expect(revoke).not.toHaveBeenCalled();

        vi.advanceTimersByTime(10_000);

        expect(revoke).toHaveBeenCalledWith('blob:file');
      } finally {
        URL.createObjectURL = original.create;
        URL.revokeObjectURL = original.revoke;
        vi.restoreAllMocks();
        vi.useRealTimers();
      }
    });

    it('names a file the server sent without a name by its period and format', () => {
      expect(fallbackFileName('2026-09-01', 'Xlsx')).toBe('report-2026-09-01.xlsx');
    });
  });
});
