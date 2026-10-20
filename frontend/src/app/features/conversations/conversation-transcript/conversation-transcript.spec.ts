import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { TranscriptMessage } from '../conversations-api';
import {
  ConversationTranscript,
  TRANSCRIPT_STEP,
  TRANSCRIPT_WINDOW,
} from './conversation-transcript';

describe('ConversationTranscript', () => {
  let fixture: ComponentFixture<ConversationTranscript>;

  const message = (
    sequence: number,
    sender: TranscriptMessage['sender'] = 'Customer',
    text = `Message ${sequence}`,
  ): TranscriptMessage => ({
    id: `m${sequence}`,
    sender,
    text,
    sentAt: '2026-10-18T10:00:00Z',
    sequence,
    sentBy: sender === 'HumanAgent' ? { userId: 'u1', email: 'sam@acme.test' } : null,
  });

  async function mount(
    messages: readonly TranscriptMessage[],
    handling: 'Ai' | 'Human' = 'Human',
  ): Promise<void> {
    fixture = TestBed.createComponent(ConversationTranscript);
    fixture.componentRef.setInput('conversationId', 'c1');
    fixture.componentRef.setInput('messages', messages);
    fixture.componentRef.setInput('handling', handling);
    fixture.componentRef.setInput('agentName', 'Concierge');
    await settle();
  }

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  const host = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const bubbles = () => [...host().querySelectorAll<HTMLElement>('li.bubble')];

  it('lets only a message that arrived while the reader is here settle in', async () => {
    await mount([message(1), message(2, 'AiAgent')]);

    expect(bubbles().some((bubble) => bubble.classList.contains('is-fresh'))).toBe(false);

    fixture.componentRef.setInput('messages', [message(1), message(2, 'AiAgent'), message(3)]);
    await settle();

    expect(bubbles().map((bubble) => bubble.classList.contains('is-fresh'))).toEqual([
      false,
      false,
      true,
    ]);
    expect(host().querySelector('[aria-live="polite"]')?.textContent).toContain(
      'New message from Customer',
    );
  });

  it('starts over for another conversation, with nothing settling in', async () => {
    await mount([message(1)]);

    fixture.componentRef.setInput('conversationId', 'c2');
    fixture.componentRef.setInput('messages', [message(7), message(8)]);
    await settle();

    expect(bubbles().some((bubble) => bubble.classList.contains('is-fresh'))).toBe(false);
  });

  it('draws the newest messages of a long conversation, and the earlier ones on request', async () => {
    const total = TRANSCRIPT_WINDOW + TRANSCRIPT_STEP + 30;
    const long = Array.from({ length: total }, (_, index) => message(index + 1));
    const showEarlier = () =>
      host().querySelector<HTMLButtonElement>('[data-testid="show-earlier"]');

    await mount(long);

    expect(bubbles()).toHaveLength(TRANSCRIPT_WINDOW);
    expect(bubbles().at(-1)?.textContent).toContain(`Message ${total}`);

    showEarlier()!.click();
    await settle();

    expect(bubbles()).toHaveLength(TRANSCRIPT_WINDOW + TRANSCRIPT_STEP);

    showEarlier()!.click();
    await settle();

    expect(bubbles()).toHaveLength(total);
    expect(showEarlier()).toBeNull();
  });

  it('says plainly that no AI reply exists, without claiming one failed', async () => {
    // The admin API records no failed turn, so the note states what is known:
    // there is no reply, and a failed AI turn sends nothing.
    await mount([message(1), message(2, 'AiAgent'), message(3)], 'Ai');

    const note = host().querySelector('[data-testid="awaiting-ai"]');

    expect(note?.textContent).toContain('The AI has not replied to the last message');
    expect(note?.textContent).not.toContain('failed');
  });

  it('adds no note while a person is answering, or once the AI has replied', async () => {
    await mount([message(1)], 'Human');
    expect(host().querySelector('[data-testid="awaiting-ai"]')).toBeNull();

    fixture.componentRef.setInput('handling', 'Ai');
    fixture.componentRef.setInput('messages', [message(1), message(2, 'AiAgent')]);
    await settle();
    expect(host().querySelector('[data-testid="awaiting-ai"]')).toBeNull();
  });

  it('names the speaker of every bubble in words, and isolates an email address', async () => {
    await mount([message(1), message(2, 'AiAgent'), message(3, 'HumanAgent')]);

    const speakers = bubbles().map(
      (bubble) => bubble.querySelector('.bubble-name')?.textContent?.trim() ?? '',
    );

    expect(speakers).toEqual(['Customer', 'Concierge', 'sam@acme.test']);
    expect(bubbles()[2].querySelector('bdi[dir="ltr"]')?.textContent).toBe('sam@acme.test');
    expect(bubbles().every((bubble) => bubble.querySelector('.message-text[dir="auto"]'))).toBe(
      true,
    );
  });

  it('draws a handoff divider where a person took over from the AI', async () => {
    await mount([message(1), message(2, 'AiAgent'), message(3), message(4, 'HumanAgent')]);

    const divider = host().querySelector('.handoff-divider');

    expect(divider?.textContent).toContain('A person took over');
    expect(divider?.classList.contains('handoff-to-human')).toBe(true);
  });

  it('is a named region the keyboard can reach while it scrolls by itself, and nothing extra otherwise', async () => {
    await mount([message(1), message(2, 'AiAgent')]);

    // Part of the page (it does not scroll by itself): no extra tab stop.
    expect(host().hasAttribute('tabindex')).toBe(false);
    expect(host().hasAttribute('role')).toBe(false);
    expect(host().hasAttribute('aria-labelledby')).toBe(false);

    fixture.componentRef.setInput('scrollRegion', true);
    await settle();

    expect(host().getAttribute('tabindex')).toBe('0');
    expect(host().getAttribute('role')).toBe('region');

    const heading = host().querySelector('h2');
    const labelledBy = host().getAttribute('aria-labelledby');

    expect(labelledBy).toBeTruthy();
    expect(heading?.id).toBe(labelledBy);
    expect(heading?.textContent?.trim()).toBe('Messages');
  });
});
