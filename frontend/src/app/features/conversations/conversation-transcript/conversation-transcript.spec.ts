import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { TranscriptEvent, TranscriptMessage } from '../conversations-api';
import { ConversationTranscript } from './conversation-transcript';

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
  const testId = <T extends HTMLElement = HTMLElement>(id: string) =>
    host().querySelector<T>(`[data-testid="${id}"]`);

  const event = (over: Partial<TranscriptEvent> & { kind: string }): TranscriptEvent => ({
    id: `e-${over.kind}`,
    at: '2026-10-18T10:00:00Z',
    afterSequence: 1,
    actor: over.kind === 'AiTurnFailed' ? null : { userId: 'u1', email: 'sam@acme.test' },
    reason: null,
    ...over,
  });

  /**
   * A scroller jsdom cannot measure: as tall as its rows, so the height changes at
   * the moment a real browser's would, when the new rows render.
   */
  function measure(element: HTMLElement, rowHeight: number, scrollTop: number): void {
    Object.defineProperty(element, 'scrollHeight', {
      configurable: true,
      get: () => element.querySelectorAll('li').length * rowHeight,
    });
    Object.defineProperty(element, 'clientHeight', { configurable: true, value: 500 });
    element.scrollTop = scrollTop;
  }

  /** Stands in for the browser's observer, so a test can say the edge came into range. */
  class FakeObserver {
    static instances: FakeObserver[] = [];

    readonly observed: Element[] = [];
    disconnected = false;

    constructor(
      readonly callback: (entries: { isIntersecting: boolean }[]) => void,
      readonly options: IntersectionObserverInit,
    ) {
      FakeObserver.instances.push(this);
    }

    observe(element: Element): void {
      this.observed.push(element);
    }

    disconnect(): void {
      this.disconnected = true;
    }

    static latest(): FakeObserver {
      return FakeObserver.instances[FakeObserver.instances.length - 1];
    }

    fire(isIntersecting = true): void {
      this.callback([{ isIntersecting }]);
    }
  }

  beforeEach(() => {
    FakeObserver.instances = [];
    vi.stubGlobal('IntersectionObserver', FakeObserver);
  });

  afterEach(() => vi.unstubAllGlobals());

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

  it('draws every message it holds, with no window of its own', () => {
    return mount(Array.from({ length: 120 }, (_, index) => message(index + 1))).then(() => {
      expect(bubbles()).toHaveLength(120);
    });
  });

  describe('going up', () => {
    let requested: number;

    async function mountLong(hasEarlier = true): Promise<void> {
      await mount([message(41), message(42)]);
      requested = 0;
      fixture.componentInstance.earlierRequested.subscribe(() => requested++);
      fixture.componentRef.setInput('hasEarlier', hasEarlier);
      await settle();
    }

    it('reads nothing just by opening, because the view goes to the end first', async () => {
      await mountLong();

      expect(requested).toBe(0);
      expect(FakeObserver.latest().observed).toContain(testId('earlier-edge'));
    });

    it('asks for the page before when the edge comes within 400 px', async () => {
      await mountLong();

      expect(FakeObserver.latest().options.rootMargin).toBe('400px 0px 0px 0px');

      FakeObserver.latest().fire();

      expect(requested).toBe(1);
    });

    it('asks nothing while the edge is out of range', async () => {
      await mountLong();

      FakeObserver.latest().fire(false);

      expect(requested).toBe(0);
    });

    it('does not ask again while a page is being read', async () => {
      await mountLong();
      fixture.componentRef.setInput('loadingEarlier', true);
      await settle();

      FakeObserver.latest().fire();
      testId<HTMLButtonElement>('show-earlier')!.click();

      expect(requested).toBe(0);
    });

    it('offers the same read from the keyboard', async () => {
      await mountLong();

      testId<HTMLButtonElement>('show-earlier')!.click();

      expect(requested).toBe(1);
    });

    it('keeps the same button through loading, so focus is not dropped', async () => {
      await mountLong();

      const button = testId<HTMLButtonElement>('show-earlier')!;

      fixture.componentRef.setInput('loadingEarlier', true);
      await settle();

      expect(testId('show-earlier')).toBe(button);
      expect(button.disabled).toBe(false);
      expect(button.getAttribute('aria-disabled')).toBe('true');
      expect(button.getAttribute('aria-busy')).toBe('true');
      expect(button.querySelector('.spinner')).not.toBeNull();
    });

    it('says why a page failed, and waits for the reader instead of trying again', async () => {
      await mountLong();
      fixture.componentRef.setInput('earlierFailure', 'Nabra could not be reached.');
      await settle();

      expect(testId('earlier-failed')?.textContent).toContain('Could not load earlier messages.');
      expect(testId('earlier-failed')?.textContent).toContain('Nabra could not be reached.');
      expect(testId('show-earlier')?.textContent).toContain('Try again');

      FakeObserver.latest().fire();
      expect(requested).toBe(0);

      testId<HTMLButtonElement>('show-earlier')!.click();
      expect(requested).toBe(1);
    });

    it('says where the conversation starts once nothing is earlier', async () => {
      await mountLong(false);

      expect(testId('transcript-start')?.textContent).toContain('Start of the conversation');
      expect(testId('show-earlier')).toBeNull();
    });

    it('keeps the reader where they were when earlier messages go in above', async () => {
      await mount([message(41), message(42)]);

      const scroller = host();

      // The host scrolls by itself here (see `scroller()`).
      vi.spyOn(window, 'getComputedStyle').mockReturnValue({
        overflowY: 'auto',
      } as CSSStyleDeclaration);
      // A day row and two bubbles, 400 px each: the reader is 900 px from the end.
      measure(scroller, 400, 300);

      fixture.componentRef.setInput('messages', [
        message(39),
        message(40),
        message(41),
        message(42),
      ]);
      await settle();

      // Five rows now: still 900 px from the end.
      expect(scroller.scrollTop).toBe(1100);
      vi.restoreAllMocks();
    });

    it('neither announces nor animates earlier messages as new', async () => {
      await mount([message(41), message(42)]);
      const unseen: number[] = [];

      fixture.componentInstance.unseenChange.subscribe((count) => unseen.push(count));
      fixture.componentRef.setInput('messages', [message(40), message(41), message(42)]);
      await settle();

      expect(bubbles().some((bubble) => bubble.classList.contains('is-fresh'))).toBe(false);
      expect(unseen).toEqual([]);
      expect(host().querySelector('[aria-live="polite"]')?.textContent).toContain(
        'Earlier messages loaded.',
      );
    });

    it('looks at the edge again after a page, in case it is still in range', async () => {
      await mountLong();

      const before = FakeObserver.instances.length;

      fixture.componentRef.setInput('messages', [message(40), message(41), message(42)]);
      await settle();

      expect(FakeObserver.instances.length).toBe(before + 1);
    });

    it('stops watching when it goes away', async () => {
      await mountLong();

      const observer = FakeObserver.latest();

      fixture.destroy();

      expect(observer.disconnected).toBe(true);
    });
  });

  it('still treats a message arriving at the end as new', async () => {
    await mount([message(1), message(2)]);

    fixture.componentRef.setInput('messages', [message(1), message(2), message(3)]);
    await settle();

    expect(bubbles().map((bubble) => bubble.classList.contains('is-fresh'))).toEqual([
      false,
      false,
      true,
    ]);
  });

  it('says plainly when no AI reply exists yet', async () => {
    await mount([message(1), message(2, 'AiAgent'), message(3)], 'Ai');

    expect(testId('awaiting-ai')?.textContent).toContain(
      'The AI has not replied to the last message yet.',
    );
  });

  it('leaves that note out when the server says the turn failed', async () => {
    await mount([message(1), message(2, 'AiAgent'), message(3)], 'Ai');

    fixture.componentRef.setInput('events', [event({ kind: 'AiTurnFailed', afterSequence: 3 })]);
    await settle();

    expect(testId('awaiting-ai')).toBeNull();
    expect(testId('transcript-event-AiTurnFailed')?.textContent).toContain(
      'The AI could not reply',
    );
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

  describe('dividers from the server', () => {
    it('names who took over, with the address isolated', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [event({ kind: 'TakenOver' })]);
      await settle();

      const divider = testId('transcript-event-TakenOver');
      const email = divider?.querySelector('bdi');

      expect(divider?.textContent?.replace(/\s+/g, ' ').trim()).toBe('sam@acme.test took over');
      expect(divider?.classList.contains('handoff-to-human')).toBe(true);
      expect(email?.getAttribute('dir')).toBe('ltr');
      expect(email?.getAttribute('translate')).toBe('no');
    });

    it('names who handed it back', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [event({ kind: 'ReturnedToAi' })]);
      await settle();

      const divider = testId('transcript-event-ReturnedToAi');

      expect(divider?.textContent?.replace(/\s+/g, ' ').trim()).toBe(
        'sam@acme.test returned it to the AI',
      );
      expect(divider?.classList.contains('handoff-to-ai')).toBe(true);
    });

    it('still says what happened when the actor is not known', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [
        event({ kind: 'TakenOver', actor: null }),
        event({ kind: 'ReturnedToAi', actor: null }),
      ]);
      await settle();

      expect(testId('transcript-event-TakenOver')?.textContent).toContain('A person took over');
      expect(testId('transcript-event-ReturnedToAi')?.textContent).toContain('Back with the AI');
    });

    it('marks a failed turn in words and in the rule, never by colour alone', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [event({ kind: 'AiTurnFailed' })]);
      await settle();

      const divider = testId('transcript-event-AiTurnFailed');

      expect(divider?.textContent).toContain('The AI could not reply');
      expect(divider?.classList.contains('transcript-failed')).toBe(true);
    });

    it('invents no divider for a conversation with no events', async () => {
      await mount([message(1), message(2, 'AiAgent'), message(3), message(4, 'HumanAgent')]);

      expect(host().querySelector('.handoff-divider')).toBeNull();
    });

    it('keeps the view at the end when a divider lands there', async () => {
      await mount([message(1)]);

      const scroller = host();

      vi.spyOn(window, 'getComputedStyle').mockReturnValue({
        overflowY: 'auto',
      } as CSSStyleDeclaration);
      // A day row and a bubble, 500 px each, with the view at the end.
      measure(scroller, 500, 500);

      fixture.componentRef.setInput('events', [event({ kind: 'TakenOver' })]);
      await settle();

      expect(scroller.scrollTop).toBe(1500);
      vi.restoreAllMocks();
    });
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
