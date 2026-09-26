import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Menu } from './menu';

@Component({
  imports: [Menu],
  template: `
    <app-menu label="Row actions">
      <span menuTrigger>More</span>
      <button type="button" role="menuitem" (click)="chosen.set('edit')">Edit</button>
      <button type="button" role="menuitem" (click)="chosen.set('copy')">Copy</button>
      <button type="button" role="menuitem" (click)="chosen.set('remove')">Remove</button>
    </app-menu>
    <button type="button" id="outside">Elsewhere</button>
  `,
})
class Host {
  readonly chosen = signal('');
}

/** The menu button pattern: every menu in the product relies on these rules. */
describe('Menu', () => {
  let fixture: ComponentFixture<Host>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const trigger = (): HTMLButtonElement => root().querySelector('[aria-haspopup="menu"]')!;
  const menu = (): HTMLElement | null => root().querySelector('[role="menu"]');
  const items = (): HTMLElement[] => [...root().querySelectorAll<HTMLElement>('[role="menuitem"]')];

  const key = (target: Element, name: string) => {
    target.dispatchEvent(new KeyboardEvent('keydown', { key: name, bubbles: true }));
    fixture.detectChanges();
  };

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    document.body.appendChild(fixture.nativeElement);
    await settle();
  });

  afterEach(() => (fixture.nativeElement as HTMLElement).remove());

  it('names the trigger and reports it collapsed', () => {
    expect(trigger().getAttribute('aria-label')).toBe('Row actions');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(menu()).toBeNull();
  });

  it('opens with focus on the first item, labelled by its trigger', async () => {
    trigger().click();
    await settle();

    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(menu()?.getAttribute('aria-labelledby')).toBe(trigger().id);
    expect(document.activeElement).toBe(items()[0]);
  });

  it('moves between items with the arrow keys, wrapping, and with Home and End', async () => {
    trigger().click();
    await settle();

    key(items()[0], 'ArrowDown');
    expect(document.activeElement).toBe(items()[1]);

    key(items()[1], 'End');
    expect(document.activeElement).toBe(items()[2]);

    key(items()[2], 'ArrowDown');
    expect(document.activeElement).toBe(items()[0]);

    key(items()[0], 'ArrowUp');
    expect(document.activeElement).toBe(items()[2]);

    key(items()[2], 'Home');
    expect(document.activeElement).toBe(items()[0]);
  });

  it('opens from the keyboard with ArrowUp on the last item', async () => {
    key(trigger(), 'ArrowUp');
    await settle();

    expect(document.activeElement).toBe(items()[2]);
  });

  it('closes on Escape and returns focus to the trigger', async () => {
    trigger().click();
    await settle();

    key(items()[1], 'Escape');
    await settle();

    expect(menu()).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('closes after an item is chosen, having run it, and returns focus', async () => {
    trigger().click();
    await settle();

    items()[1].click();
    await settle();

    expect(fixture.componentInstance.chosen()).toBe('copy');
    expect(menu()).toBeNull();
    expect(document.activeElement).toBe(trigger());
  });

  it('closes on a pointer press outside without taking focus', async () => {
    trigger().click();
    await settle();

    const outside = root().querySelector<HTMLButtonElement>('#outside')!;
    outside.dispatchEvent(new PointerEvent('pointerdown', { bubbles: true }));
    await settle();

    expect(menu()).toBeNull();
    expect(document.activeElement).not.toBe(trigger());
  });

  it('closes on Tab and lets focus move on', async () => {
    trigger().click();
    await settle();

    key(items()[0], 'Tab');
    await settle();

    expect(menu()).toBeNull();
  });
});
