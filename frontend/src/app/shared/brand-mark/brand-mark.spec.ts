import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BRAND_MARK_PATHS, BrandMark } from './brand-mark';

@Component({
  imports: [BrandMark],
  template: `<app-brand-mark [tone]="tone()" [label]="label()" />`,
})
class Host {
  readonly tone = signal<'voices' | 'mono'>('voices');
  readonly label = signal('');
}

describe('BrandMark', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const svg = (): SVGSVGElement => fixture.nativeElement.querySelector('svg');
    return { fixture, svg };
  }

  it('draws the two voices as two pre-cut paths, with no mask reference', () => {
    const { svg } = setup();
    const paths = [...svg().querySelectorAll('path')];

    expect(paths.map((p) => p.getAttribute('class'))).toEqual(['voice-ai', 'voice-human']);
    expect(paths.map((p) => p.getAttribute('d'))).toEqual([
      BRAND_MARK_PATHS.ai,
      BRAND_MARK_PATHS.human,
    ]);
    // url(#id) references break under <base href="/"> in some browsers.
    expect(svg().outerHTML).not.toContain('url(#');
  });

  it('is decorative unless it is given a name', () => {
    const { fixture, svg } = setup();

    expect(svg().getAttribute('aria-hidden')).toBe('true');
    expect(svg().hasAttribute('role')).toBe(false);

    fixture.componentInstance.label.set('OmniAI');
    fixture.detectChanges();

    expect(svg().getAttribute('role')).toBe('img');
    expect(svg().getAttribute('aria-label')).toBe('OmniAI');
    expect(svg().hasAttribute('aria-hidden')).toBe(false);
  });

  it('switches to a single colour on request', () => {
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
      expect(svg().getAttribute('viewBox')).toBe('2 4 23 23');
    } finally {
      document.documentElement.removeAttribute('dir');
    }
  });
});
