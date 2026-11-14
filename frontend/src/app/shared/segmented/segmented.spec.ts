import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Segmented } from './segmented';

@Component({
  imports: [Segmented],
  template: `
    <fieldset app-segmented data-testid="radios">
      <legend>Period</legend>
      <label class="segmented-option"
        ><input type="radio" name="p" value="7" data-testid="p7" />7 days</label
      >
      <label class="segmented-option"
        ><input type="radio" name="p" value="30" data-testid="p30" checked />30 days</label
      >
    </fieldset>
    <nav app-segmented aria-label="Views" data-testid="links">
      <a class="segmented-option" href="/a" aria-current="true">All</a>
      <a class="segmented-option" href="/b">Mine</a>
    </nav>
    <div app-segmented role="group" aria-label="Language" data-testid="buttons">
      <button
        type="button"
        class="segmented-option"
        [attr.aria-pressed]="language() === 'en'"
        (click)="language.set('en')"
      >
        English
      </button>
      <button
        type="button"
        class="segmented-option"
        lang="ar"
        [attr.aria-pressed]="language() === 'ar'"
        (click)="language.set('ar')"
      >
        العربية
      </button>
    </div>
  `,
})
class Host {
  readonly language = signal<'en' | 'ar'>('en');
}

describe('Segmented', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const byTestId = <T extends HTMLElement>(id: string): T =>
      root.querySelector<T>(`[data-testid="${id}"]`)!;
    return { fixture, root, byTestId };
  }

  it("styles the consumer's own container, keeping its element and semantics", () => {
    const { byTestId } = setup();

    expect(byTestId('radios').tagName).toBe('FIELDSET');
    expect(byTestId('links').tagName).toBe('NAV');
    expect(byTestId('buttons').getAttribute('role')).toBe('group');
    for (const id of ['radios', 'links', 'buttons']) {
      expect(byTestId(id).classList).toContain('segmented');
    }
  });

  it('keeps radios native: a segment is the label of its radio', () => {
    const { byTestId } = setup();
    const seven = byTestId<HTMLInputElement>('p7');

    seven.closest('label')!.click();

    expect(seven.checked).toBe(true);
    expect(byTestId<HTMLInputElement>('p30').checked).toBe(false);
  });

  it('marks the current link with aria-current, never role="tab"', () => {
    const { byTestId } = setup();
    const links = [...byTestId('links').querySelectorAll('a')];

    expect(links.map((a) => a.getAttribute('aria-current'))).toEqual(['true', null]);
    expect(byTestId('links').querySelector('[role="tab"]')).toBeNull();
  });

  it('presses one button at a time, in either language', () => {
    const { fixture, byTestId } = setup();
    const [english, arabic] = [...byTestId('buttons').querySelectorAll('button')];

    arabic.click();
    fixture.detectChanges();

    expect(arabic.getAttribute('aria-pressed')).toBe('true');
    expect(english.getAttribute('aria-pressed')).toBe('false');
    expect(arabic.lang).toBe('ar');
  });
});
