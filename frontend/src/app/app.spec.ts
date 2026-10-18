import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import {
  NavigationCancel,
  NavigationCancellationCode,
  NavigationEnd,
  NavigationStart,
  Router,
  provideRouter,
} from '@angular/router';
import { App, settlesFirstNavigation } from './app';

@Component({ template: '<h1>Sign in</h1>' })
class Page {}

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

  describe('the end of the loading state (Phase 36.5)', () => {
    it('keeps the lockup on screen until the first page lands, then removes it for good', async () => {
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        imports: [App],
        providers: [provideRouter([{ path: 'sign-in', component: Page }])],
      });
      const fixture = TestBed.createComponent(App);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('app-boot-state [role="status"]')?.textContent).toContain(
        'Loading',
      );

      await TestBed.inject(Router).navigateByUrl('/sign-in');
      fixture.detectChanges();

      expect(root.querySelector('app-boot-state')).toBeNull();
      expect(root.querySelector('h1')?.textContent).toBe('Sign in');
    });

    it('waits through a redirect, but not through any other ending', () => {
      const redirect = new NavigationCancel(
        1,
        '/',
        'redirect',
        NavigationCancellationCode.Redirect,
      );
      const refused = new NavigationCancel(
        1,
        '/',
        'guard said no',
        NavigationCancellationCode.GuardRejected,
      );

      expect(settlesFirstNavigation(new NavigationStart(1, '/'))).toBe(false);
      expect(settlesFirstNavigation(redirect)).toBe(false);
      expect(settlesFirstNavigation(refused)).toBe(true);
      expect(settlesFirstNavigation(new NavigationEnd(1, '/', '/sign-in'))).toBe(true);
    });
  });
});
