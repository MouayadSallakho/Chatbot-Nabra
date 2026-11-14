import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Tooltip } from './tooltip';

@Component({
  imports: [Tooltip],
  template: `
    <a
      href="/inbox"
      aria-label="Inbox"
      [appTooltip]="words()"
      [describes]="describes()"
      [placement]="placement()"
      [fixed]="fixed()"
      [edge]="edge()"
    >
      <svg aria-hidden="true"></svg>
    </a>
  `,
})
class Host {
  readonly words = signal('Inbox');
  readonly describes = signal(false);
  readonly placement = signal<'end' | 'above' | 'below'>('end');
  readonly fixed = signal(false);
  readonly edge = signal<string | null>(null);
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

  it('shows nothing when it has no words (a name already on screen, 39.3)', () => {
    const { fixture, link, tip } = setup();

    fixture.componentInstance.words.set('');
    fixture.detectChanges();
    link().dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();

    expect(tip().hidden).toBe(true);
  });

  it('can sit below its trigger, for the phone’s top bar', () => {
    const { fixture, tip } = setup();

    fixture.componentInstance.placement.set('below');
    fixture.detectChanges();

    expect(tip().getAttribute('data-placement')).toBe('below');
  });

  it('can be placed against the window, beside its trigger, for a scrolling list', () => {
    const { fixture, link, tip } = setup();

    fixture.componentInstance.fixed.set(true);
    fixture.detectChanges();
    link().dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();

    expect(tip().hidden).toBe(false);
    expect(tip().hasAttribute('data-fixed')).toBe(true);
    expect(tip().style.getPropertyValue('inset-inline-start')).toMatch(/px$/);
    expect(tip().style.getPropertyValue('inset-block-start')).toMatch(/px$/);
  });

  it('clears an ancestor’s edge when told to (the rail, not its item)', () => {
    const { fixture, link, tip } = setup();
    const wrapper = fixture.nativeElement as HTMLElement;

    wrapper.classList.add('rail');
    wrapper.getBoundingClientRect = () => ({ left: 0, right: 72 }) as DOMRect;
    link().getBoundingClientRect = () => ({ left: 16, right: 56, top: 100, height: 40 }) as DOMRect;
    fixture.componentInstance.fixed.set(true);
    fixture.componentInstance.edge.set('.rail');
    fixture.detectChanges();
    link().dispatchEvent(new MouseEvent('mouseenter'));
    fixture.detectChanges();

    expect(tip().style.getPropertyValue('inset-inline-start')).toBe('80px');
    expect(tip().style.getPropertyValue('inset-block-start')).toBe('120px');
  });
});
