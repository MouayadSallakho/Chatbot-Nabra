import { Command, foldForMatch, labelParts, matchScore, rankCommands } from './commands';

const command = (label: string, keywords: string[] = []): Command => ({
  id: label,
  group: 'navigate',
  label,
  keywords,
  icon: 'overview',
  run: () => undefined,
});

describe('palette matching', () => {
  describe('foldForMatch', () => {
    it('ignores case and Latin accents', () => {
      expect(foldForMatch('  Résumé  Builder ')).toBe('resume builder');
    });

    it('treats the Arabic letters people type interchangeably as one', () => {
      // Hamza on alef, taa marbuta, alef maqsura: typed either way, the same word.
      expect(foldForMatch('الإعدادات')).toBe(foldForMatch('الاعدادات'));
      expect(foldForMatch('مساحة')).toBe(foldForMatch('مساحه'));
      expect(foldForMatch('على')).toBe(foldForMatch('علي'));
    });

    it('drops tashkeel and tatweel', () => {
      expect(foldForMatch('الفَوْتَرَة')).toBe(foldForMatch('الفوترة'));
      expect(foldForMatch('التـقـارير')).toBe(foldForMatch('التقارير'));
    });
  });

  describe('matchScore', () => {
    it('needs every word of the query', () => {
      expect(matchScore(command('Invite a member'), 'invite member')).toBeGreaterThan(0);
      expect(matchScore(command('Invite a member'), 'invite customer')).toBe(0);
    });

    it('matches everything for an empty query', () => {
      expect(matchScore(command('Overview'), '   ')).toBeGreaterThan(0);
    });

    it('finds Arabic labels typed without hamza or diacritics', () => {
      expect(matchScore(command('إنشاء وكيل ذكاء اصطناعي'), 'انشاء')).toBeGreaterThan(0);
    });
  });

  describe('rankCommands', () => {
    it('puts a word that starts the label above one inside it, then keywords', () => {
      const ranked = rankCommands(
        [
          command('Team', ['invitations']),
          command('Pending reinvites'),
          command('Invite a member'),
        ],
        'invit',
      ).map((entry) => entry.label);

      expect(ranked).toEqual(['Invite a member', 'Pending reinvites', 'Team']);
    });

    it('keeps the given order between equal matches', () => {
      const ranked = rankCommands([command('Reports'), command('Report schedules')], 'rep');

      expect(ranked.map((entry) => entry.label)).toEqual(['Reports', 'Report schedules']);
    });

    it('leaves out what does not match', () => {
      expect(rankCommands([command('Billing'), command('Team')], 'bill')).toHaveLength(1);
    });
  });

  describe('labelParts', () => {
    it('cuts the label around the typed text, keeping its case', () => {
      expect(labelParts('Upload a knowledge file', 'KNOW')).toEqual({
        before: 'Upload a ',
        match: 'know',
        after: 'ledge file',
      });
    });

    it('emphasises nothing when the match was found only through folding', () => {
      expect(labelParts('Résumé', 'resume')).toEqual({ before: 'Résumé', match: '', after: '' });
    });

    it('never emphasises inside Arabic, whose letters must stay joined', () => {
      expect(labelParts('التحليلات', 'الت')).toEqual({ before: 'التحليلات', match: '', after: '' });
    });
  });
});
