import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Chip, ChipTone } from './chip';

@Component({
  imports: [Chip],
  template: `<app-chip class="row-reason" [tone]="tone()" [size]="size()">{{ word() }}</app-chip>`,
})
class Host {
  readonly tone = signal<ChipTone>('need');
  readonly size = signal<'sm' | 'md'>('md');
  readonly word = signal('Needs a person');
}

describe('Chip', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const chip = (): HTMLElement => fixture.nativeElement.querySelector('app-chip');
    return { fixture, chip };
  }

  it('says its meaning in a word; the dot is drawn by the stylesheet, never read', () => {
    const { chip } = setup();

    expect(chip().textContent?.trim()).toBe('Needs a person');
    expect(chip().children.length).toBe(0);
    expect(chip().classList).toContain('chip');
    expect(chip().classList).toContain('chip-need');
    expect(chip().dataset['tone']).toBe('need');
  });

  it('keeps the classes its consumer gave it', () => {
    const { fixture, chip } = setup();

    fixture.componentInstance.tone.set('failed');
    fixture.detectChanges();

    expect(chip().classList).toContain('row-reason');
    expect(chip().classList).toContain('chip-failed');
    expect(chip().classList).not.toContain('chip-need');
  });

  it('has a dense size for rows', () => {
    const { fixture, chip } = setup();

    fixture.componentInstance.size.set('sm');
    fixture.detectChanges();

    expect(chip().classList).toContain('chip-sm');
  });

  it('carries an Arabic word as it is', () => {
    const { fixture, chip } = setup();

    fixture.componentInstance.tone.set('ai');
    fixture.componentInstance.word.set('الذكاء الاصطناعي يجيب');
    fixture.detectChanges();

    expect(chip().textContent?.trim()).toBe('الذكاء الاصطناعي يجيب');
    expect(chip().classList).toContain('chip-ai');
  });

  it('draws status tones with their own glyph classes', () => {
    const { fixture, chip } = setup();

    for (const tone of ['success', 'warning', 'danger', 'info'] as const) {
      fixture.componentInstance.tone.set(tone);
      fixture.detectChanges();
      expect(chip().classList).toContain(`chip-${tone}`);
    }
  });
});
