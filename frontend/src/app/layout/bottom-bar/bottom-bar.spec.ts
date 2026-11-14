import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NavCounts } from '@app/layout/nav-counts';
import { NAV_ITEMS } from '@app/layout/navigation';
import { BottomBar } from './bottom-bar';

@Component({ template: '' })
class Blank {}

const ANALYST = ['workspace.view', 'analytics.view', 'reports.view', 'reports.generate'];

/**
 * The phone's bottom bar (Phase 39.3): four areas chosen per permission, "More"
 * with the rest in a modal sheet, the inbox's count, and where you are.
 */
describe('BottomBar', () => {
  let fixture: ComponentFixture<BottomBar>;
  let needsPerson: ReturnType<typeof signal<number | null>>;

  const original = {
    showModal: HTMLDialogElement.prototype.showModal,
    close: HTMLDialogElement.prototype.close,
  };

  // jsdom has no modal dialogs; these do what the sheet relies on.
  beforeEach(() => {
    HTMLDialogElement.prototype.showModal = function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
      this.querySelector<HTMLElement>('[autofocus]')?.focus();
    };
    HTMLDialogElement.prototype.close = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
  });

  afterEach(() => {
    HTMLDialogElement.prototype.showModal = original.showModal;
    HTMLDialogElement.prototype.close = original.close;
    root()?.remove();
  });

  const root = (): HTMLElement => fixture?.nativeElement as HTMLElement;
  const barLabels = (): string[] =>
    [...root().querySelectorAll('.bar-item .bar-label')].map((el) => el.textContent!.trim());
  const more = (): HTMLButtonElement | null =>
    root().querySelector<HTMLButtonElement>('[data-testid="nav-more"]');
  const sheet = (): HTMLDialogElement =>
    root().querySelector<HTMLDialogElement>('[data-testid="more-sheet"]')!;

  async function render(granted: readonly string[] = ['*'], url = '/overview') {
    const allows = (code: string) => granted.includes('*') || granted.includes(code);

    needsPerson = signal<number | null>(null);

    TestBed.configureTestingModule({
      providers: [
        provideRouter(NAV_ITEMS.map((item) => ({ path: item.path, component: Blank }))),
        {
          provide: PermissionService,
          useValue: { hasAny: (...codes: string[]) => codes.some(allows), has: allows },
        },
        { provide: NavCounts, useValue: { needsPerson, show: () => () => undefined } },
      ],
    });

    await TestBed.inject(Router).navigateByUrl(url);
    fixture = TestBed.createComponent(BottomBar);
    document.body.appendChild(root());
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('offers an owner the inbox, the overview, the customers, the knowledge and "More"', async () => {
    await render();

    expect(barLabels()).toEqual(['Inbox', 'Overview', 'Customers', 'Knowledge', 'More']);
    expect(root().querySelector('nav')?.getAttribute('aria-label')).toBe('Primary');
  });

  it('fills a restricted member’s bar with analytics and reports instead', async () => {
    await render(ANALYST);

    expect(barLabels()).toEqual(['Overview', 'Analytics', 'Reports', 'More']);
  });

  it('marks the current area, and only it', async () => {
    await render(['*'], '/customers');

    const current = [...root().querySelectorAll('[aria-current="page"]')].filter(
      (el) => !el.closest('dialog'),
    );

    expect(current).toHaveLength(1);
    expect(current[0].textContent).toContain('Customers');
    expect(more()?.classList).not.toContain('is-active');
  });

  it('marks "More" when the current page is one of the areas behind it', async () => {
    await render(['*'], '/billing');

    expect(more()?.classList).toContain('is-active');
  });

  it('opens "More" as a modal sheet on its first area, grouped like the side bar', async () => {
    await render();

    expect(more()?.getAttribute('aria-expanded')).toBe('false');

    more()!.click();
    fixture.detectChanges();

    expect(sheet().hasAttribute('open')).toBe(true);
    expect(more()?.getAttribute('aria-expanded')).toBe('true');
    expect(document.activeElement?.getAttribute('data-testid')).toBe('more-ai-agents');

    const labels = [...sheet().querySelectorAll('.more-group-label')].map((el) =>
      el.textContent!.trim(),
    );

    expect(labels).toEqual(['AI', 'Insights', 'Workspace']);
    expect(sheet().getAttribute('aria-labelledby')).toBe('more-sheet-title');
  });

  it('closes on Escape and returns focus to "More"', async () => {
    await render();

    more()!.click();
    fixture.detectChanges();

    const cancel = new Event('cancel', { cancelable: true });

    sheet().dispatchEvent(cancel);
    fixture.detectChanges();

    expect(cancel.defaultPrevented).toBe(true);
    expect(sheet().hasAttribute('open')).toBe(false);
    expect(document.activeElement).toBe(more());
  });

  it('keeps Tab inside the sheet, past its last area', async () => {
    await render();

    more()!.click();
    fixture.detectChanges();

    const stops = [...sheet().querySelectorAll<HTMLElement>('a[href], button')];
    const last = stops[stops.length - 1];
    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });

    last.focus();
    last.dispatchEvent(event);

    expect(event.defaultPrevented).toBe(true);
    expect(document.activeElement).toBe(stops[0]);
  });

  it('closes when an area is chosen, leaving focus to the new page', async () => {
    await render();

    more()!.click();
    fixture.detectChanges();

    await TestBed.inject(Router).navigateByUrl('/team');
    fixture.detectChanges();

    expect(sheet().hasAttribute('open')).toBe(false);
  });

  it('shows the inbox’s count on its icon, with words for a screen reader', async () => {
    await render();

    needsPerson.set(148);
    fixture.detectChanges();

    const inbox = root().querySelector('[data-testid="nav-conversations"]')!;

    expect(inbox.querySelector('.bar-count')?.textContent?.trim()).toBe('99+');
    expect(inbox.querySelector('.bar-count')?.getAttribute('aria-hidden')).toBe('true');
    expect(inbox.querySelector('.visually-hidden')?.textContent).toContain('Needs a person: 148');
  });

  it('has no "More" when every area fits in the bar', async () => {
    await render(['conversations.view']);

    expect(barLabels()).toEqual(['Inbox', 'Overview']);
    expect(more()).toBeNull();
  });
});
