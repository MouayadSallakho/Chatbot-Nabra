import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Pagination } from './pagination';

/** A list's part: it owns the page, and is busy while the next one loads. */
@Component({
  imports: [Pagination],
  template: `
    <app-pagination
      [page]="page()"
      [pageSize]="20"
      [total]="57"
      items="customers"
      [busy]="busy()"
      (pageChange)="requested.push($event)"
    />
  `,
})
class Host {
  readonly page = signal(1);
  readonly busy = signal(false);
  readonly requested: number[] = [];
}

/**
 * Focus through a page change. The pressed button keeps focus while the page
 * loads, and at either end of the list focus moves to the other button rather
 * than falling to the page body.
 */
describe('Pagination focus', () => {
  let fixture: ComponentFixture<Host>;

  const buttons = () => [
    ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLButtonElement>(
      '.pager-controls button',
    ),
  ];
  const previous = () => buttons()[0];
  const next = () => buttons()[1];

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

  it('keeps the pressed button focusable while the page loads, and ignores presses', async () => {
    next().focus();
    next().click();
    fixture.componentInstance.busy.set(true);
    await settle();

    // Not disabled -- a disabled button would drop focus to the page body.
    expect(next().disabled).toBe(false);
    expect(next().getAttribute('aria-disabled')).toBe('true');
    expect(document.activeElement).toBe(next());

    next().click();
    expect(fixture.componentInstance.requested).toEqual([2]);
  });

  it('moves focus to the other button when the end of the list disables the pressed one', async () => {
    fixture.componentInstance.page.set(2);
    await settle();

    next().focus();
    next().click();
    fixture.componentInstance.busy.set(true);
    await settle();

    // Page 3 of 3 arrives: Next is now genuinely unavailable.
    fixture.componentInstance.page.set(3);
    fixture.componentInstance.busy.set(false);
    await settle();

    expect(next().disabled).toBe(true);
    expect(document.activeElement).toBe(previous());
  });

  it('leaves focus where it is in the middle of the list', async () => {
    next().focus();
    next().click();
    fixture.componentInstance.busy.set(true);
    await settle();
    fixture.componentInstance.page.set(2);
    fixture.componentInstance.busy.set(false);
    await settle();

    expect(document.activeElement).toBe(next());
  });
});
