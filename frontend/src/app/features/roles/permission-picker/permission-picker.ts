import { ChangeDetectionStrategy, Component, computed, inject, input, output } from '@angular/core';
import { LocaleFormat } from '@app/core/i18n/locale-format';
import { PermissionDefinition } from '@app/core/authorization/permission-codes';
import { Icon } from '@app/shared/icon/icon';
import { PermissionGroup, groupId } from '../permission-groups';
import {
  ImpactHint,
  permissionDescription,
  permissionImpacts,
  permissionLabel,
} from '../permission-words';

/** One permission with everything a reader needs beside its checkbox. */
interface PickerOption {
  readonly code: string;
  readonly id: string;
  readonly label: string;
  readonly description: string;
  readonly impacts: readonly ImpactHint[];
}

interface PickerArea {
  readonly key: string;
  readonly id: string;
  readonly name: string;
  readonly options: readonly PickerOption[];
}

/**
 * The permissions of a role, by area (Phase 36.10).
 *
 * <strong>Edit</strong> draws a fieldset per area: its name, how much of it is
 * granted ("2 of 4 granted"), then a checkbox per permission with what it
 * allows and, where it matters, what it can lead to (it reads what customers
 * wrote, it costs money, it cannot be undone). The checkbox is named by the
 * permission's label alone; the sentence and the hints are its description, so
 * a screen reader hears the name first and the detail after.
 *
 * <strong>Read</strong> draws the same areas as a plain list, for a role the
 * reader may look at but not change. Not a form of disabled checkboxes: a
 * control nobody may use reads as a fault, not a rule.
 *
 * Which permissions are offered is the caller's decision (the authority
 * ceiling lives in the editor and the create page, unchanged); this only
 * draws what it is given, in the server's order.
 */
@Component({
  selector: 'app-permission-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  template: `
    @for (area of areas(); track area.key) {
      @if (mode() === 'edit') {
        <fieldset class="perm-area" [attr.data-testid]="'area-' + area.id">
          <legend class="perm-area-name">{{ area.name }}</legend>
          <p class="perm-area-count" [attr.data-testid]="'area-count-' + area.id">
            {{ countText(area) }}
          </p>

          @for (option of area.options; track option.code) {
            <label class="perm-option" [attr.data-testid]="'permission-' + option.code">
              <input
                type="checkbox"
                class="form-check-input"
                [checked]="isTicked(option.code)"
                [attr.aria-labelledby]="option.id + '-label'"
                [attr.aria-describedby]="
                  option.impacts.length
                    ? option.id + '-description ' + option.id + '-impacts'
                    : option.id + '-description'
                "
                (change)="toggled.emit({ code: option.code, ticked: $any($event.target).checked })"
              />
              <span class="perm-option-text">
                <span class="perm-label" [id]="option.id + '-label'">{{ option.label }}</span>
                <span class="perm-description" [id]="option.id + '-description'">{{
                  option.description
                }}</span>
                @if (option.impacts.length) {
                  <span class="perm-impacts" [id]="option.id + '-impacts'">
                    @for (impact of option.impacts; track impact.kind) {
                      <span class="impact" [attr.data-impact]="impact.kind">
                        <app-icon [name]="impact.icon" [strokeWidth]="1.75" />{{ impact.text }}
                      </span>
                    }
                  </span>
                }
              </span>
            </label>
          }
        </fieldset>
      } @else {
        <section class="perm-area" [attr.aria-labelledby]="area.id + '-name'">
          <h3 class="perm-area-name" [id]="area.id + '-name'">{{ area.name }}</h3>
          <ul class="perm-readout">
            @for (option of area.options; track option.code) {
              <li [attr.data-testid]="'permission-' + option.code">
                <span class="perm-label">{{ option.label }}</span>
                <span class="perm-description">{{ option.description }}</span>
                @if (option.impacts.length) {
                  <span class="perm-impacts">
                    @for (impact of option.impacts; track impact.kind) {
                      <span class="impact" [attr.data-impact]="impact.kind">
                        <app-icon [name]="impact.icon" [strokeWidth]="1.75" />{{ impact.text }}
                      </span>
                    }
                  </span>
                }
              </li>
            }
          </ul>
        </section>
      }
    }
  `,
})
export class PermissionPicker {
  private readonly locale = inject(LocaleFormat);

  readonly groups = input.required<readonly PermissionGroup[]>();
  readonly mode = input<'edit' | 'read'>('edit');

  /** The codes ticked now (edit mode). */
  readonly selected = input<ReadonlySet<string>>(new Set());

  readonly toggled = output<{ code: string; ticked: boolean }>();

  protected readonly areas = computed<readonly PickerArea[]>(() =>
    this.groups().map((group) => {
      const id = `area-${groupId(group.key)}`;

      return {
        key: group.key,
        id,
        name: group.name,
        options: group.permissions.map((permission) => this.option(permission)),
      };
    }),
  );

  protected isTicked(code: string): boolean {
    return this.selected().has(code);
  }

  protected countText(area: PickerArea): string {
    const granted = this.locale.number(
      area.options.filter((option) => this.selected().has(option.code)).length,
    );
    const total = this.locale.number(area.options.length);

    return $localize`:@@roles.area.count:${granted}:granted: of ${total}:total: granted`;
  }

  private option(permission: PermissionDefinition): PickerOption {
    return {
      code: permission.code,
      id: `perm-${permission.code.replace(/[^a-z0-9]+/g, '-')}`,
      label: permissionLabel(permission),
      description: permissionDescription(permission),
      impacts: permissionImpacts(permission.code),
    };
  }
}
