import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Orb } from './orb';
import { EventLine } from '../event-line/event-line';

@Component({
  imports: [Orb],
  template: `<app-orb [size]="size()" [thinking]="thinking()" />`,
})
class OrbHost {
  readonly size = signal<'sm' | 'lg'>('sm');
  readonly thinking = signal(false);
}

describe('Orb', () => {
  it('is always decorative, so the state is said in words beside it', () => {
    const fixture = TestBed.createComponent(OrbHost);
    fixture.detectChanges();
    const orb: HTMLElement = fixture.nativeElement.querySelector('app-orb');

    expect(orb.getAttribute('aria-hidden')).toBe('true');
    expect(orb.textContent).toBe('');
  });

  it('breathes only while the AI is answering, and grows for the greeting', () => {
    const fixture = TestBed.createComponent(OrbHost);
    fixture.detectChanges();
    const orb: HTMLElement = fixture.nativeElement.querySelector('app-orb');

    expect(orb.classList).not.toContain('is-thinking');
    fixture.componentInstance.thinking.set(true);
    fixture.componentInstance.size.set('lg');
    fixture.detectChanges();

    expect(orb.classList).toContain('is-thinking');
    expect(orb.classList).toContain('orb-lg');
  });
});

@Component({
  imports: [EventLine],
  template: `<app-event-line [tone]="tone()">{{ words() }}</app-event-line>`,
})
class LineHost {
  readonly tone = signal<'quiet' | 'failed' | 'team' | 'ai'>('failed');
  readonly words = signal('The AI could not reply · 10:41');
}

describe('EventLine', () => {
  it('is a note with words, and its tone is a class', () => {
    const fixture = TestBed.createComponent(LineHost);
    fixture.detectChanges();
    const line: HTMLElement = fixture.nativeElement.querySelector('app-event-line');

    expect(line.getAttribute('role')).toBe('note');
    expect(line.textContent?.trim()).toBe('The AI could not reply · 10:41');
    expect(line.classList).toContain('event-line');
    expect(line.classList).toContain('event-line-failed');

    fixture.componentInstance.tone.set('quiet');
    fixture.componentInstance.words.set('تمّ استلام المحادثة');
    fixture.detectChanges();

    expect(line.classList).toContain('event-line');
    expect(line.classList).not.toContain('event-line-failed');
    expect(line.textContent?.trim()).toBe('تمّ استلام المحادثة');
  });
});
