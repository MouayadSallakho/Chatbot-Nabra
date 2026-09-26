import { TestBed } from '@angular/core/testing';
import { ToastService } from './toast-service';

describe('ToastService', () => {
  let toasts: ToastService;

  beforeEach(() => {
    vi.useFakeTimers();
    toasts = TestBed.inject(ToastService);
  });

  afterEach(() => vi.useRealTimers());

  const texts = () => toasts.toasts().map((toast) => toast.text);

  it('lets a confirmation go after a few seconds', () => {
    toasts.success('Saved.');

    vi.advanceTimersByTime(4_900);
    expect(texts()).toEqual(['Saved.']);

    vi.advanceTimersByTime(200);
    expect(texts()).toEqual([]);
  });

  it('keeps an error on screen for longer', () => {
    toasts.error('Could not save.');

    vi.advanceTimersByTime(6_000);
    expect(texts()).toEqual(['Could not save.']);

    vi.advanceTimersByTime(2_100);
    expect(texts()).toEqual([]);
  });

  it('holds toasts while the reader is on them, and lets them go afterwards', () => {
    toasts.success('Saved.');
    toasts.hold();

    vi.advanceTimersByTime(20_000);
    expect(texts()).toEqual(['Saved.']);

    toasts.release();
    expect(texts()).toEqual([]);
  });

  it('does not end a toast early just because a hold began and ended', () => {
    toasts.success('Saved.');
    toasts.hold();
    toasts.release();

    vi.advanceTimersByTime(1_000);
    expect(texts()).toEqual(['Saved.']);
  });
});
