import { PermissionDefinition } from '@app/core/authorization/permission-codes';

/** One heading in the role editor, with the capabilities under it. */
export interface PermissionGroup {
  readonly name: string;
  readonly permissions: readonly PermissionDefinition[];
}

/**
 * Groups the catalogue for rendering, preserving the server's order.
 *
 * <strong>The server's order is the contract, and this must not reorder
 * it.</strong> The catalogue is published grouped, and within a group from the
 * least to the most authority, so a reader goes down the list as an escalation
 * rather than through an alphabetised jumble. Sorting here would throw that away
 * and would make the editor's layout depend on a decision nobody wrote down.
 *
 * <strong>It is not a permission matrix.</strong> There are eight capabilities;
 * a spreadsheet of roles against permissions would be a lot of interface for a
 * list a person can read in one glance, and member-to-role assignment -- the
 * thing a matrix is actually for -- does not exist yet.
 */
export function groupPermissions(
  catalogue: readonly PermissionDefinition[],
): readonly PermissionGroup[] {
  const groups: PermissionGroup[] = [];

  for (const permission of catalogue) {
    const current = groups.at(-1);

    if (current && current.name === permission.group) {
      (current.permissions as PermissionDefinition[]).push(permission);
    } else {
      groups.push({ name: permission.group, permissions: [permission] });
    }
  }

  return groups;
}
