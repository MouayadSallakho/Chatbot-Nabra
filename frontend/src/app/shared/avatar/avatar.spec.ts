import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Avatar, initialsOf } from './avatar';

describe('initialsOf', () => {
  it('takes the first letters of the first and last words', () => {
    expect(initialsOf('Layla Haddad')).toBe('LH');
    expect(initialsOf('  khalid  bin  mansour ')).toBe('KM');
  });

  it('takes the first two characters of a single word or a code', () => {
    expect(initialsOf('7KQ2MX')).toBe('7K');
    expect(initialsOf('Sara')).toBe('SA');
  });

  it('gives one letter for an Arabic name, so the letters do not join into a word', () => {
    expect(initialsOf('سارة أحمد')).toBe('س');
    expect(initialsOf('ليلى')).toBe('ل');
  });

  it('gives nothing for an empty name', () => {
    expect(initialsOf('')).toBe('');
    expect(initialsOf('   ')).toBe('');
  });
});

@Component({
  imports: [Avatar],
  template: `<app-avatar
    [name]="name()"
    [text]="text()"
    [channel]="channel()"
    [size]="size()"
    [tone]="tone()"
  />`,
})
class Host {
  readonly name = signal('Layla Haddad');
  readonly text = signal('');
  readonly channel = signal('WhatsApp');
  readonly tone = signal<'customer' | 'member'>('customer');
  readonly size = signal<'xs' | 'sm' | 'md' | 'lg'>('md');
}

describe('Avatar', () => {
  function setup() {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    const host = (): HTMLElement => fixture.nativeElement.querySelector('app-avatar');
    return { fixture, host };
  }

  it('is decorative: the row it sits in names the person and the channel', () => {
    const { host } = setup();

    expect(host().getAttribute('aria-hidden')).toBe('true');
    expect(host().querySelector('.avatar-letters')?.textContent?.trim()).toBe('LH');
    expect(host().querySelector('.avatar-letters')?.getAttribute('dir')).toBe('auto');
  });

  it("draws the channel's badge with its glyph, never the colour alone", () => {
    const { fixture, host } = setup();
    const badge = (): HTMLElement | null => host().querySelector('.avatar-badge');

    expect(badge()?.dataset['channel']).toBe('WhatsApp');
    expect(badge()?.querySelector('app-icon')).not.toBeNull();

    fixture.componentInstance.channel.set('WebChat');
    fixture.detectChanges();
    expect(badge()?.dataset['channel']).toBe('WebChat');

    fixture.componentInstance.channel.set('');
    fixture.detectChanges();
    expect(badge()).toBeNull();
  });

  it('shows a given code instead of initials, and takes its size', () => {
    const { fixture, host } = setup();

    fixture.componentInstance.text.set('7K');
    fixture.componentInstance.size.set('lg');
    fixture.detectChanges();

    expect(host().textContent?.trim()).toBe('7K');
    expect(host().classList).toContain('avatar-lg');
  });

  it('keeps an Arabic initial in its own direction', () => {
    const { fixture, host } = setup();

    fixture.componentInstance.name.set('سارة أحمد');
    fixture.detectChanges();

    expect(host().querySelector('.avatar-letters')?.textContent?.trim()).toBe('س');
  });

  it("draws a colleague beside a team reply at the orb's size, in the team's gold (39.4b)", () => {
    const { fixture, host } = setup();

    fixture.componentInstance.size.set('xs');
    fixture.componentInstance.tone.set('member');
    fixture.componentInstance.channel.set('');
    fixture.detectChanges();

    expect(host().classList).toContain('avatar-xs');
    expect(host().classList).toContain('avatar-member');
    expect(host().querySelector('.avatar-badge')).toBeNull();
  });

  it('is a customer on cream unless told otherwise', () => {
    const { host } = setup();

    expect(host().classList).not.toContain('avatar-member');
    expect(host().classList).not.toContain('avatar-xs');
  });
});
