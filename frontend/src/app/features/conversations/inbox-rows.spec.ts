import type { ConversationSummary } from './conversations-api';
import { inboxRow, memberName } from './inbox-rows';

/** The list's row model (Phase 39.4a): which chips a row shows, from the server's facts only. */
describe('inboxRow', () => {
  const base: ConversationSummary = {
    id: 'c1',
    channel: 'WhatsApp',
    status: 'Open',
    handling: 'Ai',
    startedAt: '2026-11-14T10:00:00Z',
    lastActivityAt: '2026-11-14T10:05:00Z',
    agentName: null,
    messageCount: 2,
    lastMessagePreview: 'Hi',
    lastMessageSender: 'Customer',
    assignedTo: null,
    customer: null,
    unread: false,
    takenOverBy: null,
    needsPerson: false,
    needsPersonReason: null,
  };
  const sam = { userId: 'u1', email: 'sam@acme.test', displayName: 'Sam Lee' };
  const rana = { userId: 'u2', email: 'rana@acme.test', displayName: null };

  it('says the AI is answering while it has the conversation and nothing waits', () => {
    const row = inboxRow(base, 'all');

    expect(row.voice).toBe('ai');
    expect(row.reason).toBeNull();
    expect(row.owner).toBeNull();
    expect(row.channel).toBe('WhatsApp');
    expect(row.sender).toBe('Customer');
  });

  it('puts a failed turn in coral, and drops the AI chip beside it', () => {
    const row = inboxRow({ ...base, needsPerson: true, needsPersonReason: 'AiTurnFailed' }, 'all');

    expect(row.reason).toEqual({ word: 'The AI could not reply', tone: 'failed' });
    expect(row.voice).toBeNull();
  });

  it('puts every other wait in gold', () => {
    expect(
      inboxRow({ ...base, needsPerson: true, needsPersonReason: 'ReturnedUnanswered' }, 'all')
        .reason?.tone,
    ).toBe('need');
    expect(
      inboxRow(
        { ...base, handling: 'Human', needsPerson: true, needsPersonReason: 'PersonHandling' },
        'all',
      ).reason,
    ).toEqual({ word: 'Waiting for the team', tone: 'need' });
  });

  it('names the taker once, even when somebody else is assigned (note 13)', () => {
    const row = inboxRow({ ...base, handling: 'Human', takenOverBy: sam, assignedTo: rana }, 'all');

    expect(row.owner).toEqual({ kind: 'handled', name: { text: 'Sam Lee', isEmail: false } });
    expect(row.voice).toBeNull();
  });

  it('names the assignee when nobody took it over, by email when they have no name', () => {
    const row = inboxRow({ ...base, assignedTo: rana }, 'all');

    expect(row.owner).toEqual({
      kind: 'assigned',
      name: { text: 'rana@acme.test', isEmail: true },
    });
    expect(row.voice).toBe('ai');
  });

  it('says a person answers when the API does not know who (a takeover before 37.4)', () => {
    const row = inboxRow({ ...base, handling: 'Human', lastMessageSender: 'HumanAgent' }, 'all');

    expect(row.owner).toBeNull();
    expect(row.voice).toBe('human');
    expect(row.reason).toBeNull();
  });

  it('gives every row of the needs-a-person view its reason', () => {
    expect(inboxRow(base, 'needs-person').reason?.word).toBe('Waiting for the team');
    expect(inboxRow(base, 'mine').reason).toBeNull();
  });
});

describe('memberName', () => {
  it('uses the display name, trimmed, and the email where there is none (39.1)', () => {
    expect(memberName({ userId: 'u', email: 'a@b.test', displayName: '  Lina ' })).toEqual({
      text: 'Lina',
      isEmail: false,
    });
    expect(memberName({ userId: 'u', email: 'a@b.test', displayName: '   ' })).toEqual({
      text: 'a@b.test',
      isEmail: true,
    });
    expect(memberName({ userId: 'u', email: 'a@b.test' })).toEqual({
      text: 'a@b.test',
      isEmail: true,
    });
  });
});
