import { PermissionDefinition } from '@app/core/authorization/permission-codes';
import { areaName } from './permission-words';

/** One area of the role editor, with the capabilities under it. */
export interface PermissionGroup {
  /** The server's stable `groupKey`, used for tracking and element ids. */
  readonly key: string;

  /** The area's name in the reader's language. */
  readonly name: string;
  readonly permissions: readonly PermissionDefinition[];
}

/**
 * Groups the catalogue by area, preserving the server's order.
 *
 * <strong>The server's order is the contract, and this must not reorder
 * it.</strong> The catalogue is published grouped, and within a group from the
 * least to the most authority, so a reader goes down an area as an escalation
 * (view, then create, then change, then switch on) rather than through an
 * alphabetised jumble. Sorting here would throw that away.
 *
 * <strong>Areas, not a spreadsheet.</strong> Twenty-nine capabilities in eleven
 * areas are read as eleven short lists, each saying how much of it a role
 * grants; a grid of roles against permissions would be a lot of interface for
 * a decision made one role at a time.
 */
export function groupPermissions(
  catalogue: readonly PermissionDefinition[],
): readonly PermissionGroup[] {
  const groups: { key: string; name: string; permissions: PermissionDefinition[] }[] = [];

  for (const permission of catalogue) {
    const current = groups.at(-1);

    if (current && current.key === permission.groupKey) {
      current.permissions.push(permission);
    } else {
      groups.push({
        key: permission.groupKey,
        name: areaName(permission),
        permissions: [permission],
      });
    }
  }

  return groups;
}

/** A group's key as an element id fragment: lower-case letters, digits and hyphens. */
export function groupId(key: string): string {
  return key
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
}
