import { ComponentFixture, TestBed } from '@angular/core/testing';
import type { TranscriptEvent, TranscriptMessage } from '../conversations-api';
import { ConversationTranscript, SEEN_HOLD_MS } from './conversation-transcript';

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
  /** One row per message (Calm Glass, 39.4b): the bubble, its meta line and the speaker's mark. */
  const bubbles = () => [...host().querySelectorAll<HTMLElement>('li.message')];
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

  describe('seeing the newest message', () => {
    let seen: number[];
    let visibility: DocumentVisibilityState;
    let focused: boolean;

    beforeEach(() => {
      seen = [];
      visibility = 'visible';
      focused = true;
      vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility);
      vi.spyOn(document, 'hasFocus').mockImplementation(() => focused);
    });

    afterEach(() => {
      vi.useRealTimers();
      vi.restoreAllMocks();
    });

    /** The observer of the newest row: the one before the edge's. */
    const newestObserver = (): FakeObserver =>
      FakeObserver.instances[FakeObserver.instances.length - 2];

    const inView = (ratio = 0.6, height = 40) =>
      newestObserver().callback([
        {
          isIntersecting: ratio > 0,
          intersectionRatio: ratio,
          intersectionRect: { height },
          rootBounds: { height: 600 },
        } as unknown as { isIntersecting: boolean },
      ]);

    async function opened(): Promise<void> {
      await mount([message(1), message(2)]);
      fixture.componentInstance.newestSeen.subscribe((sequence) => seen.push(sequence));
      vi.useFakeTimers();
    }

    it('watches the newest row against the element that scrolls', async () => {
      await opened();

      expect(newestObserver().observed[0]).toBe(bubbles().at(-1));
    });

    it('counts after a second in view', async () => {
      await opened();
      inView();

      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS - 1);
      expect(seen).toEqual([]);

      await vi.advanceTimersByTimeAsync(1);
      expect(seen).toEqual([2]);
    });

    it('does not count a row less than half visible', async () => {
      await opened();
      inView(0.3);

      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      expect(seen).toEqual([]);
    });

    it('counts a message taller than the view once it fills half of it', async () => {
      await opened();
      inView(0.2, 400);

      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS);

      expect(seen).toEqual([2]);
    });

    it('does not count when it scrolled away before the second was up', async () => {
      await opened();
      inView();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS / 2);

      inView(0);
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      expect(seen).toEqual([]);
    });

    it('does not count in a hidden tab', async () => {
      await opened();
      visibility = 'hidden';
      inView();

      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      expect(seen).toEqual([]);
    });

    it('does not count while another window has the focus', async () => {
      await opened();
      focused = false;
      inView();

      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      expect(seen).toEqual([]);
    });

    it('counts once the person comes back to it', async () => {
      await opened();
      focused = false;
      inView();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      focused = true;
      window.dispatchEvent(new Event('focus'));
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS);

      expect(seen).toEqual([2]);
    });

    it('stops the wait when the tab is hidden', async () => {
      await opened();
      inView();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS / 2);

      visibility = 'hidden';
      document.dispatchEvent(new Event('visibilitychange'));
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      expect(seen).toEqual([]);
    });

    it('says a sequence once, and again after the person looked away and back', async () => {
      await opened();
      inView();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 3);

      expect(seen).toEqual([2]);

      inView(0);
      inView();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS);

      expect(seen).toEqual([2, 2]);
    });

    it('watches the new newest row when a message arrives', async () => {
      await opened();
      inView();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS);
      vi.useRealTimers();

      fixture.componentRef.setInput('messages', [message(1), message(2), message(3)]);
      await settle();

      expect(FakeObserver.latest().observed[0]).toBe(bubbles().at(-1));
    });

    it('stops everything when it goes away', async () => {
      await opened();
      inView();

      fixture.destroy();
      await vi.advanceTimersByTimeAsync(SEEN_HOLD_MS * 2);

      expect(seen).toEqual([]);
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

  it('names the speaker of every bubble in words under it, and isolates an email address', async () => {
    await mount([message(1), message(2, 'AiAgent'), message(3, 'HumanAgent')]);

    const metas = bubbles().map((bubble) =>
      bubble.querySelector('.message-meta')?.textContent?.replace(/\s+/g, ' ').trim(),
    );

    // The customer's is said to a screen reader only (board Inbox: the time alone).
    expect(metas[0]).toMatch(/^Customer, \d{1,2}:\d{2}\s?[AP]M$/);
    expect(metas[1]).toMatch(/^AI, Concierge · \d{1,2}:\d{2}\s?[AP]M$/);
    expect(metas[2]).toMatch(/^Team, sam@acme\.test · \d{1,2}:\d{2}\s?[AP]M$/);
    expect(bubbles()[2].querySelector('bdi[dir="ltr"]')?.textContent).toBe('sam@acme.test');
    expect(
      bubbles().every((bubble) => bubble.querySelector('.bubble .message-text[dir="auto"]')),
    ).toBe(true);
  });

  describe('a pane that changes height (39.4b)', () => {
    let resized: (() => void) | null;

    beforeEach(() => {
      resized = null;
      vi.stubGlobal(
        'ResizeObserver',
        class {
          constructor(callback: () => void) {
            resized = callback;
          }

          observe(): void {}

          disconnect(): void {}
        },
      );
    });

    afterEach(() => vi.restoreAllMocks());

    async function scrolling(scrollTop: number): Promise<HTMLElement> {
      await mount([message(1), message(2), message(3)]);

      const scroller = host();

      vi.spyOn(window, 'getComputedStyle').mockReturnValue({
        overflowY: 'auto',
      } as CSSStyleDeclaration);
      // A day row and three bubbles, 400 px each: 1,600 px, 500 in view.
      measure(scroller, 400, scrollTop);
      scroller.dispatchEvent(new Event('scroll'));

      return scroller;
    }

    it('keeps a reader who was at the end at the end when the header grows', async () => {
      const scroller = await scrolling(1100);

      // The header grew: the pane is shorter, and the end moved out of view.
      Object.defineProperty(scroller, 'clientHeight', { configurable: true, value: 380 });
      resized?.();

      expect(scroller.scrollTop).toBe(1600);
    });

    it('leaves a reader who scrolled up where they are', async () => {
      const scroller = await scrolling(200);

      Object.defineProperty(scroller, 'clientHeight', { configurable: true, value: 380 });
      resized?.();

      expect(scroller.scrollTop).toBe(200);
    });
  });

  describe('the three voices (Calm Glass, 39.4b)', () => {
    const named = (sequence: number): TranscriptMessage => ({
      ...message(sequence, 'HumanAgent', 'Done.'),
      sentBy: { userId: 'u2', email: 'sara@acme.test', displayName: 'سارة الحربي' },
    });

    it('puts the customer on the start side and the AI and the team on the end side', async () => {
      await mount([message(1), message(2, 'AiAgent'), message(3, 'HumanAgent')]);

      const [customer, ai, team] = bubbles();

      expect(customer.classList.contains('message-customer')).toBe(true);
      expect(customer.querySelector('.bubble-customer')).not.toBeNull();
      expect(ai.classList.contains('message-ai')).toBe(true);
      expect(ai.querySelector('.bubble-ai')).not.toBeNull();
      expect(team.classList.contains('message-team')).toBe(true);
      expect(team.querySelector('.bubble-team')).not.toBeNull();
    });

    it('marks the AI with the still orb and a colleague with a gold avatar; the customer with neither', async () => {
      await mount([message(1), message(2, 'AiAgent'), message(3, 'HumanAgent')]);

      const [customer, ai, team] = bubbles();

      expect(customer.querySelector('.message-mark')).toBeNull();
      expect(ai.querySelector('app-orb.message-mark')).not.toBeNull();
      expect(ai.querySelector('app-orb')?.classList.contains('is-thinking')).toBe(false);
      expect(ai.querySelector('app-orb')?.getAttribute('aria-hidden')).toBe('true');
      expect(team.querySelector('app-avatar.message-mark.avatar-member.avatar-xs')).not.toBeNull();
      expect(team.querySelector('app-avatar')?.textContent?.trim()).toBe('S');
    });

    it('names a colleague by display name, in its own direction', async () => {
      await mount([message(1), named(2)]);

      const author = testId('message-author');

      expect(author?.textContent?.trim()).toBe('سارة الحربي');
      expect(author?.getAttribute('dir')).toBe('auto');
      expect(bubbles()[1].querySelector('app-avatar')?.textContent?.trim()).toBe('س');
    });

    it('never leaves a team reply without an author', async () => {
      await mount([message(1), { ...message(2, 'HumanAgent'), sentBy: null }]);

      expect(testId('message-author')?.textContent?.trim()).toBe('Team');
    });

    it('waits with the breathing orb on the AI side, in words', async () => {
      await mount([message(1)], 'Ai');

      const tail = testId('awaiting-ai');

      expect(tail?.textContent).toContain('The AI has not replied to the last message yet.');
      expect(tail?.querySelector('app-orb')?.classList.contains('is-thinking')).toBe(true);
    });

    it('owes no AI reply to a conversation returned to it unanswered', async () => {
      await mount([message(1), message(2, 'AiAgent'), message(3)], 'Ai');
      fixture.componentRef.setInput('needsPersonReason', 'ReturnedUnanswered');
      await settle();

      expect(testId('awaiting-ai')).toBeNull();
    });

    it('says the day as a quiet heading', async () => {
      await mount([message(1)]);

      expect(host().querySelector('.transcript-day h3')?.textContent?.trim()).toBeTruthy();
    });
  });

  describe('dividers from the server', () => {
    it('names who took over, with the address isolated', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [event({ kind: 'TakenOver' })]);
      await settle();

      const divider = testId('transcript-event-TakenOver');
      const email = divider?.querySelector('bdi');

      expect(divider?.textContent?.replace(/\s+/g, ' ').trim()).toMatch(
        /^sam@acme\.test took over · \d{1,2}:\d{2}\s?[AP]M$/,
      );
      expect(divider?.querySelector('time')?.getAttribute('datetime')).toBe('2026-10-18T10:00:00Z');
      expect(divider?.classList.contains('handoff-to-human')).toBe(true);
      expect(email?.getAttribute('dir')).toBe('ltr');
      expect(email?.getAttribute('translate')).toBe('no');
    });

    it('names who handed it back', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [event({ kind: 'ReturnedToAi' })]);
      await settle();

      const divider = testId('transcript-event-ReturnedToAi');

      expect(divider?.textContent?.replace(/\s+/g, ' ').trim()).toMatch(
        /^sam@acme\.test returned it to the AI · \d{1,2}:\d{2}\s?[AP]M$/,
      );
      expect(divider?.classList.contains('handoff-to-ai')).toBe(true);
    });

    it('names who took over by display name when they have one', async () => {
      await mount([message(1)]);
      fixture.componentRef.setInput('events', [
        event({
          kind: 'TakenOver',
          actor: { userId: 'u2', email: 'sara@acme.test', displayName: 'Sara' },
        }),
      ]);
      await settle();

      const name = testId('transcript-event-TakenOver')?.querySelector('bdi');

      expect(name?.textContent?.trim()).toBe('Sara');
      expect(name?.getAttribute('dir')).toBe('auto');
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
