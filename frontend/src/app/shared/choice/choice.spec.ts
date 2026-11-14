import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { Check, Radio, Switch } from './choice';

@Component({
  imports: [Check, Radio, Switch, ReactiveFormsModule],
  template: `
    <label app-check data-testid="role-option-1">
      <input type="checkbox" id="agree" [formControl]="agree" data-testid="agree" />
      <span class="words">I agree</span>
    </label>

    <fieldset>
      <legend>How often</legend>
      <label app-radio
        ><input type="radio" name="often" value="Weekly" data-testid="weekly" checked /> Weekly
      </label>
      <label app-radio
        ><input type="radio" name="often" value="Monthly" data-testid="monthly" /> Monthly
      </label>
    </fieldset>

    <label app-switch>
      <input
        type="checkbox"
        role="switch"
        data-testid="sounds"
        [checked]="on()"
        [disabled]="locked()"
        (change)="on.set(!on())"
      />
      <span>Sounds</span>
    </label>

    <input type="checkbox" data-testid="bare" />
  `,
})
class Host {
  readonly agree = new FormControl(false, { nonNullable: true });
  readonly on = signal(false);
  readonly locked = signal(false);
}

/** The choice-control family (Phase 38 notes 3 and 4): native controls, one look. */
describe('Choice controls', () => {
  let fixture: ComponentFixture<Host>;
  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const byTestId = <T extends HTMLElement>(id: string): T =>
    root().querySelector<T>(`[data-testid="${id}"]`)!;

  beforeEach(async () => {
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
  });

  it("keeps the consumer's own label and input, with their test ids", () => {
    const label = byTestId<HTMLLabelElement>('role-option-1');
    const input = byTestId<HTMLInputElement>('agree');

    expect(label.tagName).toBe('LABEL');
    expect(label.classList).toContain('choice');
    expect(label.classList).toContain('choice-check');
    // The input sits inside the label, so the whole label is its target and its name.
    expect(label.contains(input)).toBe(true);
    expect(input.labels?.[0]).toBe(label);
    expect(label.querySelector('.choice-label')?.textContent?.trim()).toBe('I agree');
  });

  it('puts the input first and the words after it, in both directions', () => {
    const label = byTestId<HTMLLabelElement>('role-option-1');

    expect(label.firstElementChild?.tagName).toBe('INPUT');
    document.documentElement.setAttribute('dir', 'rtl');
    try {
      expect(label.firstElementChild?.tagName).toBe('INPUT');
    } finally {
      document.documentElement.removeAttribute('dir');
    }
  });

  it('toggles from a click on the words, and the form control follows', () => {
    const words = root().querySelector<HTMLElement>('.words')!;

    words.click();
    fixture.detectChanges();

    expect(fixture.componentInstance.agree.value).toBe(true);
    expect(byTestId<HTMLInputElement>('agree').checked).toBe(true);
  });

  it('draws radios that stay one native group', () => {
    const weekly = byTestId<HTMLInputElement>('weekly');
    const monthly = byTestId<HTMLInputElement>('monthly');

    expect(weekly.closest('label')?.classList).toContain('choice-radio');
    monthly.closest('label')!.click();

    expect(monthly.checked).toBe(true);
    expect(weekly.checked).toBe(false);
  });

  it('keeps the switch role on the native checkbox, and its disabled state', () => {
    const sounds = byTestId<HTMLInputElement>('sounds');

    expect(sounds.closest('label')?.classList).toContain('choice-switch');
    expect(sounds.getAttribute('role')).toBe('switch');

    sounds.click();
    fixture.detectChanges();
    expect(fixture.componentInstance.on()).toBe(true);

    fixture.componentInstance.locked.set(true);
    fixture.detectChanges();
    expect(sounds.disabled).toBe(true);
  });

  it('leaves a checkbox outside the family alone (no global rule, no escape class)', () => {
    const bare = byTestId<HTMLInputElement>('bare');

    expect(bare.closest('.choice')).toBeNull();
    expect(bare.classList.length).toBe(0);
  });
});
