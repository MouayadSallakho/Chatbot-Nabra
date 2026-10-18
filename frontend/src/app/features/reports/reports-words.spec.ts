import { ReportExecution } from './reports-api';
import { failureSentence, formatLabel, reportTitle, sizeLabel, statusLabel } from './reports-words';

/** Reports' words: translated by stable code or type, never guessed for an unknown one. */
describe('reports words', () => {
  const failed = {
    status: 'Failed',
    failureCode: 'report_too_large',
    failureReason: 'Report exceeds the supported export size. Choose a shorter period.',
  } as ReportExecution;

  it('names a known report by its type and keeps the server title for an unknown one', () => {
    expect(reportTitle('AiHumanHandling', 'AI and human handling')).toBe('AI and team handling');
    expect(reportTitle('FutureReport', 'A future report')).toBe('A future report');
  });

  it('says why a report failed by its code, with a generic sentence for an unknown code', () => {
    expect(failureSentence(failed)).toContain('Choose a shorter period');
    expect(
      failureSentence({ ...failed, failureCode: 'something_new', failureReason: 'x' }),
    ).toContain('could not be generated');
    expect(
      failureSentence({ ...failed, status: 'Completed', failureCode: null, failureReason: null }),
    ).toBeNull();
  });

  it('writes sizes in the reader’s language with Western digits, and a dash with no file', () => {
    expect(sizeLabel(512)).toMatch(/512\s*byte/);
    expect(sizeLabel(4970)).toMatch(/4\.9\s*kB/);
    expect(sizeLabel(4970, 'ar')).toContain('4.9');
    expect(sizeLabel(4970, 'ar')).not.toMatch(/[٠-٩]/);
    expect(sizeLabel(null)).toBe('—');
  });

  it('gives every status a word, and every format its name', () => {
    expect(statusLabel('Pending')).toBe('Waiting');
    expect(statusLabel('Completed')).toBe('Ready');
    expect(formatLabel('Xlsx')).toBe('Excel (XLSX)');
    expect(formatLabel('Csv')).toBe('CSV');
  });
});
