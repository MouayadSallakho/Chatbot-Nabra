import { EVERY_PERMISSION } from '@app/testing/test-doubles';
import { groupPermissions } from './permission-groups';
import { accessChangeSentence, areaName, memberCount, permissionLabel } from './permission-words';

/** Areas and permissions by their stable keys (Phase 38.4); the Arabic is in `arabic-render.spec.ts`. */
describe('permission words', () => {
  const KEYS: Readonly<Record<string, string>> = {
    workspace: 'Workspace',
    'ai-agents': 'AI agents',
    roles: 'Roles and permissions',
    members: 'Team members',
    conversations: 'Conversations',
    customers: 'Customers',
    channels: 'Channels',
    knowledge: 'Knowledge',
    analytics: 'Analytics',
    reports: 'Reports',
    billing: 'Billing',
  };

  it.each(Object.entries(KEYS))('names the %s area by its key', (groupKey, words) => {
    expect(areaName({ groupKey, group: 'The server English' })).toBe(words);
  });

  it('keeps the server English for an area this build has no words for', () => {
    expect(areaName({ groupKey: 'integrations', group: 'Integrations' })).toBe('Integrations');
  });

  it('names an area by its key even when the English group is reworded', () => {
    // The English is prose and free to change; the key is the contract.
    expect(areaName({ groupKey: 'members', group: 'People' })).toBe('Team members');
  });

  it('groups the catalogue by key, in the server order', () => {
    const groups = groupPermissions(EVERY_PERMISSION);

    expect(groups.map((group) => group.key)).toEqual([
      'workspace',
      'ai-agents',
      'roles',
      'members',
      'conversations',
      'customers',
      'channels',
      'knowledge',
      'analytics',
      'reports',
      'billing',
    ]);
    expect(groups[0].permissions.map((permission) => permission.code)).toEqual([
      'workspace.view',
      'workspace.manage',
    ]);
  });

  it('words workspace.manage by its code, and keeps the server label for an unknown code', () => {
    const manage = EVERY_PERMISSION.find((permission) => permission.code === 'workspace.manage')!;

    expect(permissionLabel({ ...manage, label: 'Server label' })).toBe('Manage workspace');
    expect(
      permissionLabel({ ...manage, code: 'workspace.archive', label: 'Archive workspace' }),
    ).toBe('Archive workspace');
  });

  it.each([
    [0, 'No members'],
    [1, '1 member'],
    [2, '2 members'],
    [1234, '1,234 members'],
  ])('counts %i holders as "%s"', (count, words) => {
    expect(memberCount(count, 'en')).toBe(words);
  });

  it.each([
    [0, "No member holds this role yet, so saving changes nobody's access."],
    [1, 'Saving changes access for 1 member.'],
    [12, 'Saving changes access for 12 members.'],
  ])('says the reach of a save for %i holders', (count, words) => {
    expect(accessChangeSentence(count, 'en')).toBe(words);
  });
});
