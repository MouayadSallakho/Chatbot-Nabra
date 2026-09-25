import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ScrollRegion } from './scroll-region';

@Component({
  imports: [ScrollRegion],
  template: `<div appScrollRegion="Generated reports table"><table></table></div>`,
})
class Host {}

/**
 * A wide table must be reachable from the keyboard, and a narrow one must not
 * gain a pointless tab stop.
 */
describe('ScrollRegion', () => {
  let callbacks: (() => void)[];
  const original = globalThis.ResizeObserver;

  beforeEach(() => {
    callbacks = [];

    // jsdom has no layout, so the observer is replaced by one the test drives.
    globalThis.ResizeObserver = class {
      constructor(callback: () => void) {
        callbacks.push(callback);
      }

      observe(): void {}

      disconnect(): void {}

      unobserve(): void {}
    } as unknown as typeof ResizeObserver;
  });

  afterEach(() => {
    globalThis.ResizeObserver = original;
  });

  async function render(scrollWidth: number, clientWidth: number): Promise<HTMLElement> {
    const fixture = TestBed.createComponent(Host);
    const region = fixture.nativeElement.querySelector('div') as HTMLElement;

    Object.defineProperty(region, 'scrollWidth', { configurable: true, get: () => scrollWidth });
    Object.defineProperty(region, 'clientWidth', { configurable: true, get: () => clientWidth });

    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    return region;
  }

  it('becomes a focusable, named region while its content overflows', async () => {
    const region = await render(1200, 390);

    expect(region.getAttribute('tabindex')).toBe('0');
    expect(region.getAttribute('role')).toBe('region');
    expect(region.getAttribute('aria-label')).toBe('Generated reports table');
  });

  it('adds no tab stop and no landmark when everything fits', async () => {
    const region = await render(800, 800);

    expect(region.hasAttribute('tabindex')).toBe(false);
    expect(region.hasAttribute('role')).toBe(false);
    expect(region.hasAttribute('aria-label')).toBe(false);
  });

  it('follows a resize', async () => {
    let width = 800;
    const fixture = TestBed.createComponent(Host);
    const region = fixture.nativeElement.querySelector('div') as HTMLElement;

    Object.defineProperty(region, 'scrollWidth', { configurable: true, get: () => 1200 });
    Object.defineProperty(region, 'clientWidth', { configurable: true, get: () => width });

    width = 1200;
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(region.hasAttribute('tabindex')).toBe(false);

    // The window narrows to a phone.
    width = 390;
    callbacks.forEach((callback) => callback());
    fixture.detectChanges();

    expect(region.getAttribute('tabindex')).toBe('0');
  });
});
