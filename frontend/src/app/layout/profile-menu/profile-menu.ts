import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ThemeService } from '@app/core/theme/theme-service';
import { LanguageService } from '@app/core/i18n/language-service';
import {
  APP_LANGUAGES,
  AppLanguage,
  LANGUAGE_ENDONYMS,
  directionOf,
} from '@app/core/i18n/language';
import { THEME_LABELS, THEME_PREFERENCES, ThemePreference } from '@app/core/theme/theme';
import { Icon } from '@app/shared/icon/icon';
import { IconName } from '@app/shared/icon/icon-paths';
import { Menu } from '@app/shared/menu/menu';

const THEME_ICONS: Readonly<Record<ThemePreference, IconName>> = {
  system: 'monitor',
  light: 'sun',
  dark: 'moon',
};

/**
 * The account menu at the end of the top bar.
 *
 * Who is signed in, which workspace they are in, their appearance and language,
 * and the ways out -- and nothing else. It is not a settings page: anything a
 * workspace administers lives in the sidebar, under Workspace.
 *
 * Every value shown is real. The initials come from the account's own address;
 * there is no invented display name, role or avatar image.
 */
@Component({
  selector: 'app-profile-menu',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Menu, Icon, RouterLink],
  templateUrl: './profile-menu.html',
  styleUrl: './profile-menu.scss',
})
export class ProfileMenu {
  readonly email = input('');
  readonly workspaceName = input('');
  readonly canSwitchWorkspace = input(false);

  readonly workspaceSwitched = output<void>();
  readonly signedOut = output<void>();

  private readonly theme = inject(ThemeService);

  private readonly languageService = inject(LanguageService);

  protected readonly preference = this.theme.preference;

  /** The language this page runs in; a different choice reloads the page. */
  protected readonly language = this.languageService.current;

  protected readonly languages = APP_LANGUAGES.map((value) => ({
    value,
    name: LANGUAGE_ENDONYMS[value],
    dir: directionOf(value),
    // A one-glance script sample where the theme items have an icon.
    glyph: value === 'ar' ? 'ع' : 'Aa',
  }));

  protected readonly themes = THEME_PREFERENCES.map((value) => ({
    value,
    label: THEME_LABELS[value],
    icon: THEME_ICONS[value],
  }));

  /** One or two letters from the address, never a guess at a real name. */
  protected readonly initials = computed(() => {
    const local = this.email().split('@')[0] ?? '';
    const parts = local.split(/[._-]+/).filter(Boolean);
    const letters = parts.length > 1 ? parts[0][0] + parts[1][0] : local.slice(0, 1);

    return letters.toUpperCase() || '?';
  });

  /**
   * The trigger's name starts with the initials it shows (WCAG 2.5.3, Phase
   * 36.11): somebody using speech input reads "AO" on the button and says it.
   */
  protected readonly label = computed(() =>
    this.email()
      ? $localize`:@@account.menuLabelFor:${this.initials()}:initials:: Account menu for ${this.email()}:email:`
      : $localize`:@@account.menuLabel:Account menu`,
  );

  protected selectTheme(preference: ThemePreference): void {
    this.theme.select(preference);
  }

  protected selectLanguage(language: AppLanguage): void {
    this.languageService.choose(language);
  }
}
