import { channelStatusTone, channelStatusWord } from './channel-words';

/**
 * Channels' status words and chips. The Arabic of each word is checked in the
 * catalogue spec («تعمل» for Active since 39.8b, never «مفعّلة»).
 */
describe('channel words', () => {
  it('names each status, and shows an unknown one as the server sent it', () => {
    expect(channelStatusWord('Draft')).toBe('Draft');
    expect(channelStatusWord('Active')).toBe('Active');
    expect(channelStatusWord('Disabled')).toBe('Disabled');
    expect(channelStatusWord('Paused')).toBe('Paused');
  });

  it('gives each status a chip with its own marker: a tick, a hollow ring, a dot', () => {
    expect(channelStatusTone('Active')).toBe('chip-success');
    expect(channelStatusTone('Draft')).toBe('chip-neutral chip-draft');
    expect(channelStatusTone('Disabled')).toBe('chip-neutral');
    expect(channelStatusTone('Paused')).toBe('chip-neutral');
  });
});
