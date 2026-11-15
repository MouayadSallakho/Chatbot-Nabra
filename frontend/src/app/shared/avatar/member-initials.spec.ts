import { memberInitials, memberShownName } from './member-initials';

describe('member initials and shown name (Phase 39.6a)', () => {
  it('takes the first letters of a chosen name, in its own script', () => {
    expect(memberInitials('Sara Ahmed', 'sara@example.test')).toBe('SA');
    expect(memberInitials('omar', 'omar@example.test')).toBe('O');
    expect(memberInitials('ليلى حداد', 'layla@example.test')).toBe('لح');
  });

  it("falls back to the address's local part, never a guess at a name", () => {
    expect(memberInitials(null, 'p36.owner@omniai.example')).toBe('PO');
    expect(memberInitials('  ', 'bob@example.test')).toBe('B');
    expect(memberInitials(undefined, '')).toBe('?');
  });

  it('shows the chosen name, else the address', () => {
    expect(memberShownName(' Sara Ahmed ', 'sara@example.test')).toBe('Sara Ahmed');
    expect(memberShownName(null, 'sara@example.test')).toBe('sara@example.test');
    expect(memberShownName('', 'sara@example.test')).toBe('sara@example.test');
  });
});
