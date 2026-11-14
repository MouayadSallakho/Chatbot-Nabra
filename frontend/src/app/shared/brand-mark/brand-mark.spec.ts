import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { APP_LANGUAGE, AppLanguage } from '@app/core/i18n/language';
import { BrandLockup } from './brand-lockup';
import { BrandMark } from './brand-mark';
import { BRAND_MARK, BRAND_NAMES } from './brand-geometry';

@Component({
  imports: [BrandMark],
  template: `<app-brand-mark [tone]="tone()" [label]="label()" />`,
})
class Host {
  readonly tone = signal<'colour' | 'mono'>('colour');
  readonly label = signal('');
}

describe('BrandMark', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const svg = (): SVGSVGElement => fixture.nativeElement.querySelector('svg');
    return { fixture, svg };
  }

  it('draws two circles and the lens where they overlap (brand.md, direction A)', () => {
    const { svg } = setup();
    const circles = [...svg().querySelectorAll('circle')];

    expect(svg().getAttribute('viewBox')).toBe('0 0 140 120');
    expect(circles.map((c) => c.getAttribute('class'))).toEqual(['mark-ai', 'mark-team']);
    expect(
      circles.map((c) => [c.getAttribute('cx'), c.getAttribute('cy'), c.getAttribute('r')]),
    ).toEqual([
      ['52', '60', '40'],
      ['88', '60', '40'],
    ]);
    expect(svg().querySelector('path.mark-lens')?.getAttribute('d')).toBe(BRAND_MARK.lens);
    // No gradient and no url(#id) reference (they break under <base href="/">).
    expect(svg().outerHTML).not.toContain('url(#');
    expect(svg().querySelector('linearGradient, radialGradient')).toBeNull();
  });

  it('is decorative unless it is given a name', () => {
    const { fixture, svg } = setup();

    expect(svg().getAttribute('aria-hidden')).toBe('true');
    expect(svg().hasAttribute('role')).toBe(false);

    fixture.componentInstance.label.set('Nabra');
    fixture.detectChanges();

    expect(svg().getAttribute('role')).toBe('img');
    expect(svg().getAttribute('aria-label')).toBe('Nabra');
    expect(svg().hasAttribute('aria-hidden')).toBe(false);
  });

  it('switches to one colour on request', () => {
    const { fixture } = setup();
    const host = (): HTMLElement => fixture.nativeElement.querySelector('app-brand-mark');

    expect(host().classList.contains('is-mono')).toBe(false);

    fixture.componentInstance.tone.set('mono');
    fixture.detectChanges();

    expect(host().classList.contains('is-mono')).toBe(true);
  });

  it('is never mirrored for right-to-left reading', () => {
    document.documentElement.setAttribute('dir', 'rtl');
    try {
      const { svg } = setup();

      expect(svg().getAttribute('transform')).toBeNull();
      expect(svg().getAttribute('viewBox')).toBe('0 0 140 120');
    } finally {
      document.documentElement.removeAttribute('dir');
    }
  });
});

describe('BrandLockup', () => {
  function setup(language: AppLanguage) {
    TestBed.configureTestingModule({ providers: [{ provide: APP_LANGUAGE, useValue: language }] });
    const fixture = TestBed.createComponent(BrandLockup);
    fixture.detectChanges();
    const host = fixture.nativeElement as HTMLElement;
    const names = (): string[] =>
      [...host.querySelectorAll('svg.lockup-name path')].map((p) => p.getAttribute('d') ?? '');
    return { fixture, host, names };
  }

  it('is one named image: the mark and both names, outlined', () => {
    const { host, names } = setup('en');

    expect(host.getAttribute('role')).toBe('img');
    expect(host.getAttribute('aria-label')).toBe('Nabra');
    expect(host.querySelector('app-brand-mark')).not.toBeNull();
    // Paths, not text: no font is downloaded for the logo.
    expect(host.querySelector('text')).toBeNull();
    expect(host.textContent?.trim()).toBe('');
    expect(names()).toEqual([BRAND_NAMES.latin.path, BRAND_NAMES.arabic.path]);
  });

  it('puts the Arabic name first on Arabic pages', () => {
    const { names } = setup('ar');

    expect(names()).toEqual([BRAND_NAMES.arabic.path, BRAND_NAMES.latin.path]);
  });

  it('lets a caller choose which name leads', () => {
    const { fixture, names } = setup('en');

    fixture.componentRef.setInput('lead', 'ar');
    fixture.detectChanges();

    expect(names()[0]).toBe(BRAND_NAMES.arabic.path);
  });

  it('sizes each name by its own advance, in em', () => {
    const { host } = setup('en');
    const [latin] = [...host.querySelectorAll<SVGElement>('svg.lockup-name')];

    expect(latin.style.inlineSize).toBe(`${BRAND_NAMES.latin.advance / 1000}em`);
  });
});
