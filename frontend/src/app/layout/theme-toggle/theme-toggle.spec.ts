import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { ThemeService } from '@app/core/theme/theme-service';
import { THEME_STORAGE_KEY } from '@app/core/theme/theme';
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { ThemeToggle } from './theme-toggle';

/**
 * The theme control's job is to make all three options visible, selectable and
 * announced. Its keyboard behaviour is the browser's -- these are real radio
 * inputs in a real fieldset -- so what is worth testing is that the semantics
 * which make that work are actually present.
 */
describe('ThemeToggle', () => {
  let fixture: ComponentFixture<ThemeToggle>;
  let storage: Storage;

  function radios(): HTMLInputElement[] {
    return [
      ...(fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>(
        'input[type="radio"]',
      ),
    ];
  }

  function labelFor(value: string): string {
    const input = radios().find((radio) => radio.value === value);

    return input?.closest('label')?.textContent?.trim() ?? '';
  }

  beforeEach(async () => {
    storage = memoryStorage();

    TestBed.configureTestingModule({
      imports: [ThemeToggle],
      providers: [
        {
          provide: MEDIA_QUERY_FACTORY,
          useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
        },
        { provide: LOCAL_STORAGE, useValue: storage },
      ],
    });

    fixture = TestBed.createComponent(ThemeToggle);
    await fixture.whenStable();
  });

  it('offers exactly the three supported modes', () => {
    expect(radios().map((radio) => radio.value)).toEqual(['system', 'light', 'dark']);
  });

  it('groups them as one choice, with a name', () => {
    const host = fixture.nativeElement as HTMLElement;

    // A fieldset with a legend is what makes a screen reader announce
    // "Theme, System, radio button, 1 of 3" rather than three loose controls.
    expect(host.querySelector('fieldset')).not.toBeNull();
    expect(host.querySelector('legend')?.textContent?.trim()).toBe('Theme');
    expect(new Set(radios().map((radio) => radio.name)).size).toBe(1);
  });

  it('labels every option with visible text, not only an icon', () => {
    expect(labelFor('system')).toBe('System');
    expect(labelFor('light')).toBe('Light');
    expect(labelFor('dark')).toBe('Dark');
  });

  it('starts on System', () => {
    const checked = radios().filter((radio) => radio.checked);

    expect(checked).toHaveLength(1);
    expect(checked[0].value).toBe('system');
  });

  it('applies and persists a chosen mode', async () => {
    const dark = radios().find((radio) => radio.value === 'dark');

    dark!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(TestBed.inject(ThemeService).preference()).toBe('dark');
    expect(storage.getItem(THEME_STORAGE_KEY)).toBe('dark');
  });

  it('moves the checked state to the chosen mode', async () => {
    radios()
      .find((radio) => radio.value === 'light')!
      .click();
    await fixture.whenStable();
    fixture.detectChanges();

    const checked = radios().filter((radio) => radio.checked);

    expect(checked).toHaveLength(1);
    expect(checked[0].value).toBe('light');
  });
});
