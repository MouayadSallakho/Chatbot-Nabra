import { QUIET_WINDOW_MS, SoundCandidate, SoundScheduler } from './sound-rules';

describe('SoundScheduler', () => {
  let played: string[];
  let scheduler: SoundScheduler;

  const ask = (sound: SoundCandidate['sound']): boolean => scheduler.request({ sound });

  beforeEach(() => {
    vi.useFakeTimers();
    played = [];
    scheduler = new SoundScheduler((candidate) =>
      played.push(candidate.test ? `test:${candidate.sound}` : candidate.sound),
    );
  });

  afterEach(() => vi.useRealTimers());

  it('plays a lone sound on the next turn', () => {
    expect(ask('new-message')).toBe(true);
    expect(played).toEqual([]);

    vi.advanceTimersByTime(0);

    expect(played).toEqual(['new-message']);
  });

  it('coalesces a burst and plays the most important: AI failed first', () => {
    ask('new-message');
    ask('ai-failed');
    ask('needs-person');
    vi.advanceTimersByTime(0);

    expect(played).toEqual(['ai-failed']);
  });

  it('prefers needs a person to a new message', () => {
    ask('new-message');
    ask('needs-person');
    vi.advanceTimersByTime(0);

    expect(played).toEqual(['needs-person']);
  });

  it('never plays two sounds inside two seconds', () => {
    ask('new-message');
    vi.advanceTimersByTime(0);
    expect(ask('ai-failed')).toBe(true);

    vi.advanceTimersByTime(QUIET_WINDOW_MS - 1);
    expect(played).toEqual(['new-message']);

    vi.advanceTimersByTime(1);
    expect(played).toEqual(['new-message', 'ai-failed']);
  });

  it('drops one no better than what has just played', () => {
    ask('needs-person');
    vi.advanceTimersByTime(0);

    expect(ask('needs-person')).toBe(false);
    expect(ask('new-message')).toBe(false);

    vi.advanceTimersByTime(QUIET_WINDOW_MS * 2);

    expect(played).toEqual(['needs-person']);
  });

  it('lets a better candidate replace the one waiting', () => {
    ask('new-message');
    vi.advanceTimersByTime(0);
    ask('needs-person');
    ask('ai-failed');
    vi.advanceTimersByTime(QUIET_WINDOW_MS);

    expect(played).toEqual(['new-message', 'ai-failed']);
  });

  it('plays anything again once the window has passed', () => {
    ask('ai-failed');
    vi.advanceTimersByTime(0);
    vi.advanceTimersByTime(QUIET_WINDOW_MS);
    ask('new-message');
    vi.advanceTimersByTime(0);

    expect(played).toEqual(['ai-failed', 'new-message']);
  });

  it('holds the test button above every sound, and repeats of it are dropped', () => {
    ask('ai-failed');
    vi.advanceTimersByTime(0);

    expect(scheduler.request({ sound: 'needs-person', test: true })).toBe(true);
    expect(scheduler.request({ sound: 'needs-person', test: true })).toBe(false);

    vi.advanceTimersByTime(QUIET_WINDOW_MS);

    expect(played).toEqual(['ai-failed', 'test:needs-person']);
  });
});
