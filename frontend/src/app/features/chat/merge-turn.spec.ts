import type { ChatMessage, ChatTurn } from './hosted-chat-api';
import { mergeTurn } from './merge-turn';

describe('mergeTurn', () => {
  const message = (id: string, sender: ChatMessage['sender'], text: string): ChatMessage => ({
    id,
    sender,
    text,
    sentAt: '2026-11-13T10:00:00Z',
  });

  const earlier = message('m0', 'AiAgent', 'Hello, how can I help?');
  const asked = message('m1', 'Customer', 'When do you close?');
  const answered = message('m2', 'AiAgent', 'We close at six.');

  const aiTurn: ChatTurn = { customerMessage: asked, reply: answered, handling: 'Ai' };
  const personTurn: ChatTurn = { customerMessage: asked, reply: null, handling: 'Human' };

  const ids = (list: readonly ChatMessage[]) => list.map((m) => m.id);

  it('adds the message and the reply after what is already there, in that order', () => {
    expect(ids(mergeTurn([earlier], aiTurn))).toEqual(['m0', 'm1', 'm2']);
  });

  it('adds only the message when a person is handling the conversation', () => {
    expect(ids(mergeTurn([earlier], personTurn))).toEqual(['m0', 'm1']);
  });

  it('adds only the reply when the stored message was already read', () => {
    // The answer after the "stored" re-read: the message is there, the reply is not.
    expect(ids(mergeTurn([earlier, asked], aiTurn))).toEqual(['m0', 'm1', 'm2']);
  });

  it('adds nothing when the settled re-read already brought both', () => {
    const current = [earlier, asked, answered];

    expect(mergeTurn(current, aiTurn)).toBe(current);
  });

  it('changes nothing when the same answer arrives again', () => {
    const once = mergeTurn([earlier], aiTurn);

    expect(mergeTurn(once, aiTurn)).toBe(once);
    expect(ids(mergeTurn(once, aiTurn))).toEqual(['m0', 'm1', 'm2']);
  });

  it('keeps the place a re-read gave a message', () => {
    // A person's reply read after the stored message stays where the server put it.
    const fromPerson = message('m3', 'HumanAgent', 'A colleague here.');

    expect(ids(mergeTurn([asked, answered, fromPerson], aiTurn))).toEqual(['m1', 'm2', 'm3']);
  });

  it('starts an empty transcript', () => {
    expect(ids(mergeTurn([], aiTurn))).toEqual(['m1', 'm2']);
  });
});
