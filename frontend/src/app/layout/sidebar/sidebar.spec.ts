import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { PermissionService } from '@app/core/authorization/permission-service';
import { NAV_ITEMS } from '@app/layout/navigation';
import { PEEK_CLOSE_DELAY_MS, PEEK_OPEN_DELAY_MS, Sidebar } from './sidebar';

/**
 * The rail's hover-intent behaviour, driven with fake timers.
 *
 * Assertions are on the panel's expanded state, which is what decides whether
 * labels are visible -- never on pixel values or animation frames.
 */
describe('Sidebar', () => {
  let fixture: ComponentFixture<Sidebar>;

  const panel = (): HTMLElement =>
    (fixture.nativeElement as HTMLElement).querySelector('.sidebar') as HTMLElement;
  const expanded = (): boolean => panel().classList.contains('is-expanded');

  const pointer = (type: 'pointerenter' | 'pointerleave', pointerType = 'mouse') => {
    panel().dispatchEvent(new PointerEvent(type, { pointerType }));
    fixture.detectChanges();
  };

  const advance = (ms: number) => {
    vi.advanceTimersByTime(ms);
    fixture.detectChanges();
  };

  async function render(inputs: { pinned?: boolean; drawer?: boolean } = {}) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: PermissionService, useValue: { hasAny: () => true } },
      ],
    });

    fixture = TestBed.createComponent(Sidebar);
    fixture.componentRef.setInput('pinned', inputs.pinned ?? false);
    fixture.componentRef.setInput('drawer', inputs.drawer ?? false);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it('opens only after the pointer rests on the rail', async () => {
    await render();

    pointer('pointerenter');
    advance(PEEK_OPEN_DELAY_MS - 20);
    expect(expanded()).toBe(false);

    advance(40);
    expect(expanded()).toBe(true);
  });

  it('does not open for a pointer that only sweeps across the rail', async () => {
    await render();

    pointer('pointerenter');
    advance(60);
    pointer('pointerleave');
    advance(PEEK_OPEN_DELAY_MS * 2);

    expect(expanded()).toBe(false);
  });

  it('closes a little after the pointer leaves, not at once', async () => {
    await render();
    pointer('pointerenter');
    advance(PEEK_OPEN_DELAY_MS);

    pointer('pointerleave');
    advance(PEEK_CLOSE_DELAY_MS - 20);
    expect(expanded()).toBe(true);

    advance(40);
    expect(expanded()).toBe(false);
  });

  it('cancels a pending close when the pointer comes straight back', async () => {
    await render();
    pointer('pointerenter');
    advance(PEEK_OPEN_DELAY_MS);

    pointer('pointerleave');
    advance(PEEK_CLOSE_DELAY_MS / 2);
    pointer('pointerenter');
    advance(PEEK_CLOSE_DELAY_MS * 2);

    expect(expanded()).toBe(true);
  });

  it('gives touch no hover behaviour at all', async () => {
    await render();

    pointer('pointerenter', 'touch');
    advance(PEEK_OPEN_DELAY_MS * 3);

    expect(expanded()).toBe(false);
  });

  it('is permanently expanded when pinned, and hover changes nothing', async () => {
    await render({ pinned: true });

    expect(expanded()).toBe(true);
    expect(panel().classList.contains('is-rail')).toBe(false);

    pointer('pointerleave');
    advance(PEEK_CLOSE_DELAY_MS * 2);
    expect(expanded()).toBe(true);
  });

  it('keeps every link named while collapsed to the rail', async () => {
    await render();

    const names = [...panel().querySelectorAll('.sidebar-nav .nav-label')].map((label) =>
      label.textContent?.trim(),
    );

    expect(expanded()).toBe(false);
    expect(names).toEqual(NAV_ITEMS.map((item) => item.label));
  });

  it('offers a pin control on the desktop, and none in the drawer', async () => {
    await render();
    expect(panel().querySelector('.pin-toggle')?.textContent?.trim()).toBe('Pin sidebar');

    fixture.componentRef.setInput('drawer', true);
    fixture.detectChanges();

    expect(panel().querySelector('.pin-toggle')).toBeNull();
  });
});
