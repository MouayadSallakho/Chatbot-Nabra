import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Tooltip } from './tooltip';

@Component({
  imports: [Tooltip],
  template: `
    <a href="/inbox" aria-label="Inbox" [appTooltip]="words()" [describes]="describes()">
      <svg aria-hidden="true"></svg>
    </a>
  `,
})
class Host {
  readonly words = signal('Inbox');
  readonly describes = signal(false);
}

describe('Tooltip', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const link = (): HTMLAnchorElement => fixture.nativeElement.querySelector('a');
    const tip = (): HTMLElement => link().querySelector('[role="tooltip"]')!;
    return { fixture, link, tip };
  }

  it('is hidden until hover, and never changes the trigger’s name', () => {
    const { fixture, link, tip } = setup();

    expect(tip().hidden).toBe(true);
    expect(tip().getAttribute('aria-hidden')).toBe('true');
    expect(link().getAttribute('aria-label')).toBe('Inbox');

    link().dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();
    expect(tip().hidden).toBe(false);
    expect(tip().textContent).toBe('Inbox');

    link().dispatchEvent(new MouseEvent('mouseleave'));
    fixture.detectChanges();
    expect(tip().hidden).toBe(true);
  });

  it('shows on keyboard focus and Escape dismisses it without moving focus', () => {
    const { fixture, link, tip } = setup();

    link().focus();
    link().dispatchEvent(new FocusEvent('focusin', { bubbles: true }));
    fixture.detectChanges();
    expect(tip().hidden).toBe(false);

    link().dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();
    expect(tip().hidden).toBe(true);
    expect(document.activeElement).toBe(link());
  });

  it('describes the trigger only when it adds to the name, in any language', () => {
    const { fixture, link, tip } = setup();

    expect(link().hasAttribute('aria-describedby')).toBe(false);

    fixture.componentInstance.describes.set(true);
    fixture.componentInstance.words.set('المحادثات التي تحتاج إلى شخص');
    fixture.detectChanges();

    expect(link().getAttribute('aria-describedby')).toBe(tip().id);
    expect(tip().textContent).toBe('المحادثات التي تحتاج إلى شخص');
  });

  it('is removed with its trigger', () => {
    const { fixture, tip } = setup();
    const element = tip();

    fixture.destroy();

    expect(element.isConnected).toBe(false);
  });
});
