import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import type { MessageSender } from '@app/features/conversations/conversations-api';
import { replyRuns, voiceOf } from './voice';
import { VoiceChip } from './voice-chip';
import { VoiceSplit } from './voice-split';
import { VoiceTrack } from './voice-track';

const said = (...senders: MessageSender[]) => senders.map((sender) => ({ sender }));

describe('voiceOf', () => {
  it('gives the AI and people a voice, and nobody else', () => {
    expect(voiceOf('AiAgent')).toBe('ai');
    expect(voiceOf('HumanAgent')).toBe('human');
    expect(voiceOf('Customer')).toBeNull();
    expect(voiceOf('System')).toBeNull();
  });
});

describe('replyRuns', () => {
  it('folds consecutive replies by one voice into a run, skipping customers and notices', () => {
    const runs = replyRuns(
      said(
        'Customer',
        'AiAgent',
        'Customer',
        'AiAgent',
        'System',
        'HumanAgent',
        'HumanAgent',
        'AiAgent',
      ),
    );

    expect(runs).toEqual([
      { voice: 'ai', replies: 2 },
      { voice: 'human', replies: 2 },
      { voice: 'ai', replies: 1 },
    ]);
  });

  it('is empty when nobody has replied', () => {
    expect(replyRuns(said('Customer', 'System', 'Customer'))).toEqual([]);
    expect(replyRuns([])).toEqual([]);
  });
});

describe('VoiceChip', () => {
  @Component({
    imports: [VoiceChip],
    template: `<app-voice-chip [voice]="voice()" [label]="label()" />`,
  })
  class Host {
    readonly voice = signal<'ai' | 'human'>('ai');
    readonly label = signal('');
  }

  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const chip = (): HTMLElement => fixture.nativeElement.querySelector('app-voice-chip');
    return { fixture, chip };
  }

  it('says the voice in a word beside its glyph, never by colour alone', () => {
    const { chip } = setup();

    expect(chip().textContent?.trim()).toBe('AI');
    expect(chip().querySelector('svg')).not.toBeNull();
    expect(chip().classList).toContain('voice-chip-ai');
    expect(chip().dataset['voice']).toBe('ai');
  });

  it('changes voice in place, which is what the "Take over" sweep animates', () => {
    const { fixture, chip } = setup();
    const before = chip();

    fixture.componentInstance.voice.set('human');
    fixture.detectChanges();

    expect(chip()).toBe(before);
    expect(chip().textContent?.trim()).toBe('Person');
    expect(chip().classList).toContain('voice-chip-human');
    expect(chip().classList).not.toContain('voice-chip-ai');
  });

  it('can carry a more specific word, such as a colleague', () => {
    const { fixture, chip } = setup();

    fixture.componentInstance.voice.set('human');
    fixture.componentInstance.label.set('sara@example.test');
    fixture.detectChanges();

    expect(chip().textContent?.trim()).toBe('sara@example.test');
    expect(chip().classList).toContain('voice-chip-human');
  });
});

describe('VoiceSplit', () => {
  @Component({
    imports: [VoiceSplit],
    template: `<app-voice-split
      [ai]="ai()"
      [human]="human()"
      noun="open conversations"
      nounOne="open conversation"
    />`,
  })
  class Host {
    readonly ai = signal(3);
    readonly human = signal(1);
  }

  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const el = (): HTMLElement => fixture.nativeElement;
    return { fixture, el };
  }

  it('draws one part per voice, sized by its count', () => {
    const { el } = setup();
    const ai = el().querySelector<HTMLElement>('.voice-split-ai');
    const human = el().querySelector<HTMLElement>('.voice-split-human');

    expect(ai?.style.flexGrow).toBe('3');
    expect(human?.style.flexGrow).toBe('1');
  });

  it('tells assistive technology one sentence, and hides the bar and legend from it', () => {
    const { el } = setup();

    expect(el().querySelector('.visually-hidden')?.textContent).toBe(
      '4 open conversations: 3 with the AI, 1 with people on the team.',
    );
    expect(el().querySelector('.voice-split-bar')?.getAttribute('aria-hidden')).toBe('true');
    expect(el().querySelector('.voice-split-legend')?.getAttribute('aria-hidden')).toBe('true');
  });

  it('shows the legend counts the caller gave, with a word for each voice', () => {
    const { el } = setup();
    const items = [...el().querySelectorAll('.voice-split-legend li')].map((li) => [
      li.querySelector('app-voice-chip')?.textContent?.trim(),
      li.querySelector('.voice-split-count')?.textContent?.trim(),
    ]);

    expect(items).toEqual([
      ['AI', '3'],
      ['Person', '1'],
    ]);
  });

  it('draws no part for a voice with nothing, and says so when both are empty', () => {
    const { fixture, el } = setup();

    fixture.componentInstance.ai.set(0);
    fixture.componentInstance.human.set(1);
    fixture.detectChanges();

    expect(el().querySelector('.voice-split-ai')).toBeNull();
    expect(el().querySelector('.visually-hidden')?.textContent).toBe(
      '1 open conversation: 0 with the AI, 1 with people on the team.',
    );

    fixture.componentInstance.human.set(0);
    fixture.detectChanges();

    expect(el().querySelectorAll('.voice-split-part').length).toBe(0);
    expect(el().querySelector('.visually-hidden')?.textContent).toBe(
      'No open conversations yet, with the AI or with people.',
    );
  });
});

describe('VoiceTrack', () => {
  @Component({
    imports: [VoiceTrack],
    template: `<app-voice-track [messages]="messages()" [partial]="partial()" />`,
  })
  class Host {
    readonly messages = signal(said('Customer', 'AiAgent', 'AiAgent', 'Customer', 'HumanAgent'));
    readonly partial = signal(false);
  }

  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const el = (): HTMLElement => fixture.nativeElement;
    return { fixture, el };
  }

  it('draws the real runs of reply authors, in order', () => {
    const { el } = setup();
    const runs = [...el().querySelectorAll<HTMLElement>('.voice-track-run')];

    expect(runs.map((r) => (r.classList.contains('voice-track-ai') ? 'ai' : 'human'))).toEqual([
      'ai',
      'human',
    ]);
    expect(runs.map((r) => r.style.flexGrow)).toEqual(['2', '1']);
    expect(el().querySelector('.visually-hidden')?.textContent).toBe(
      'Replies so far: AI, 2 replies, then Person, 1 reply.',
    );
  });

  it('says when it describes only the loaded part of a transcript', () => {
    const { fixture, el } = setup();

    fixture.componentInstance.partial.set(true);
    fixture.detectChanges();

    expect(el().querySelector('.visually-hidden')?.textContent).toMatch(
      /^Replies in the messages loaded so far:/,
    );
  });

  it('renders nothing it cannot derive: no track before anybody has replied', () => {
    const { fixture, el } = setup();

    fixture.componentInstance.messages.set(said('Customer', 'System'));
    fixture.detectChanges();

    expect(el().querySelector('.voice-track')).toBeNull();
    expect(el().querySelector('.visually-hidden')).toBeNull();
    expect(el().querySelector('app-voice-track')?.classList).toContain('is-empty');
  });
});
