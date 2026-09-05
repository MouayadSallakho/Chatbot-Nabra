import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ThemeService } from '@app/core/theme/theme-service';
import { THEME_LABELS, THEME_PREFERENCES, ThemePreference } from '@app/core/theme/theme';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';

/** How each option is presented. */
interface ThemeOption {
  readonly value: ThemePreference;
  readonly label: string;
  readonly icon: IconName;
}

const THEME_ICONS: Readonly<Record<ThemePreference, IconName>> = {
  system: 'monitor',
  light: 'sun',
  dark: 'moon',
};

/**
 * The theme control: a segmented switch over System, Light and Dark.
 *
 * BUILT ON NATIVE RADIOS, which is the whole design decision. A `role="radio"`
 * implementation would have to reimplement arrow-key movement, roving tabindex,
 * the checked state and form semantics -- four things browsers already do
 * correctly and that hand-rolled versions habitually get wrong. The inputs are
 * visually hidden and the labels are styled, so the appearance is ours and the
 * behaviour is the platform's.
 *
 * All three options are always visible. A single button that cycles through
 * three states would be smaller, but it would never show what the other two are,
 * and it could not show that `System` is a distinct mode rather than whichever
 * of light or dark the machine happens to be in.
 */
@Component({
  selector: 'app-theme-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  templateUrl: './theme-toggle.html',
  styleUrl: './theme-toggle.scss',
})
export class ThemeToggle {
  private readonly theme = inject(ThemeService);

  protected readonly preference = this.theme.preference;

  protected readonly options: readonly ThemeOption[] = THEME_PREFERENCES.map((value) => ({
    value,
    label: THEME_LABELS[value],
    icon: THEME_ICONS[value],
  }));

  protected select(preference: ThemePreference): void {
    this.theme.select(preference);
  }
}
