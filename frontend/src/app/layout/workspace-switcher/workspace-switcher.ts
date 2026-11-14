import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { Tenant } from '@app/core/api/tenant-api';
import { Icon } from '@app/shared/icon/icon';
import { Menu } from '@app/shared/menu/menu';

/** A workspace's mark: its own first letter, never an invented logo. */
export function workspaceInitial(name: string): string {
  return ([...name.trim()][0] ?? '').toLocaleUpperCase();
}

/**
 * The workspace switcher (Phase 39.3, board Inbox): which workspace every screen
 * is about, and, when the account belongs to more than one, a glass menu of
 * them.
 *
 * - **panel** (the side bar): a 44px panel button with the workspace's letter,
 *   its name and a chevron;
 * - **rail** (the collapsed side bar): the letter alone, named in full;
 * - **bar** (the phone's top bar): the name as a quiet line under the area.
 *
 * With one workspace it is not a control at all, only the name: there is
 * nowhere to switch to. Choosing another workspace is the chooser's own path
 * (the shell leaves the current page first, so an unsaved-changes guard still
 * asks), and "Choose or create a workspace" opens the chooser itself.
 */
@Component({
  selector: 'app-workspace-switcher',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Menu, Icon],
  templateUrl: './workspace-switcher.html',
  styleUrl: './workspace-switcher.scss',
  host: { '[class]': "'is-' + variant()" },
})
export class WorkspaceSwitcher {
  /** The workspace being administered; empty while it loads. */
  readonly name = input('');

  /** Its id, to mark it in the list. */
  readonly currentId = input<string | null>(null);

  /** Every workspace the account belongs to, in the server's order. */
  readonly workspaces = input<readonly Tenant[]>([]);

  readonly variant = input<'panel' | 'rail' | 'bar'>('panel');

  /** Another workspace was chosen. */
  readonly chosen = output<Tenant>();

  /** The reader asked for the chooser (to see every workspace or create one). */
  readonly chooserRequested = output<void>();

  protected readonly canSwitch = computed(() => this.workspaces().length > 1);

  protected readonly initial = computed(() => workspaceInitial(this.name()));

  protected readonly options = computed(() =>
    this.workspaces().map((workspace) => ({
      workspace,
      initial: workspaceInitial(workspace.name),
      current: workspace.id === this.currentId(),
    })),
  );

  /**
   * The rail's trigger shows only the letter, so it is named in full. The same
   * words the panel's trigger says, visibly or hidden: "Workspace: <name>.
   * Switch workspace".
   */
  protected readonly railLabel = computed(
    () =>
      `${$localize`:@@shell.workspacePrefix:Workspace: `}${this.name()}. ${$localize`:@@shell.switchWorkspace:Switch workspace`}`,
  );

  protected choose(option: { readonly workspace: Tenant; readonly current: boolean }): void {
    if (!option.current) {
      this.chosen.emit(option.workspace);
    }
  }
}
