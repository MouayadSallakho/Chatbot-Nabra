import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ThemeService } from '@app/core/theme/theme-service';
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
 * Who is signed in, which workspace they are in, their appearance preference,
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

  protected readonly preference = this.theme.preference;

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

  protected readonly label = computed(() =>
    this.email() ? `Account menu for ${this.email()}` : 'Account menu',
  );

  protected selectTheme(preference: ThemePreference): void {
    this.theme.select(preference);
  }
}
