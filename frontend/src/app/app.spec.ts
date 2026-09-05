import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { App } from './app';

/**
 * The root is deliberately almost nothing: the landmarks, navigation and layout
 * all moved into the routed shell, so that a future phase can render a sign-in
 * screen outside the shell without unpicking anything here.
 *
 * These tests assert exactly that, and no more. Everything the application shows
 * is covered where it lives -- see app-shell.spec.ts.
 */
describe('App', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([])],
    });
  });

  it('creates', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders only a routed outlet', async () => {
    const fixture = TestBed.createComponent(App);
    await fixture.whenStable();

    const compiled = fixture.nativeElement as HTMLElement;

    // No heading, no chrome, no "just one" global control. If this starts
    // failing, something has begun accumulating in the root again.
    expect(compiled.querySelector('h1')).toBeNull();
    expect(compiled.querySelector('header')).toBeNull();
    expect(compiled.querySelector('nav')).toBeNull();
  });
});
