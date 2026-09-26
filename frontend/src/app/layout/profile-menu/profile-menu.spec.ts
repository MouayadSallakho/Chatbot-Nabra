import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MEDIA_QUERY_FACTORY, MediaQueries } from '@app/core/media/media';
import { LOCAL_STORAGE } from '@app/core/storage/local-storage';
import { ThemeService } from '@app/core/theme/theme-service';
import { FakeMediaMatcher, memoryStorage } from '@app/testing/test-doubles';
import { ProfileMenu } from './profile-menu';

describe('ProfileMenu', () => {
  let fixture: ComponentFixture<ProfileMenu>;

  const root = (): HTMLElement => fixture.nativeElement as HTMLElement;
  const trigger = (): HTMLButtonElement => root().querySelector('[aria-haspopup="menu"]')!;

  async function render(email = 'ada.owner@omniai.example', canSwitch = false) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: LOCAL_STORAGE, useValue: memoryStorage() },
        {
          provide: MEDIA_QUERY_FACTORY,
          useValue: new FakeMediaMatcher({ [MediaQueries.prefersDark]: false }).factory,
        },
      ],
    });

    fixture = TestBed.createComponent(ProfileMenu);
    fixture.componentRef.setInput('email', email);
    fixture.componentRef.setInput('workspaceName', 'Acme Trading');
    fixture.componentRef.setInput('canSwitchWorkspace', canSwitch);
    document.body.appendChild(fixture.nativeElement);
    fixture.detectChanges();
    await fixture.whenStable();
  }

  async function open(): Promise<void> {
    trigger().click();
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => root().remove());

  it('names the trigger after the account, with initials drawn from the address', async () => {
    await render();

    expect(trigger().getAttribute('aria-label')).toBe('Account menu for ada.owner@omniai.example');
    expect(root().querySelector('.avatar')?.textContent?.trim()).toBe('AO');
  });

  it('shows who is signed in and where, and never an invented name or role', async () => {
    await render();
    await open();

    expect(root().querySelector('.identity')?.textContent).toContain('ada.owner@omniai.example');
    expect(root().querySelector('.identity')?.textContent).toContain('Acme Trading');
  });

  it('chooses a theme through checked radio items', async () => {
    await render();
    await open();

    const radios = [...root().querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')];
    expect(radios.map((radio) => radio.textContent?.trim())).toEqual(['System', 'Light', 'Dark']);
    expect(radios[0].getAttribute('aria-checked')).toBe('true');

    radios[2].click();
    fixture.detectChanges();

    expect(TestBed.inject(ThemeService).preference()).toBe('dark');
  });

  it('signs out through a real menu item', async () => {
    await render();
    const signedOut = vi.fn();
    fixture.componentInstance.signedOut.subscribe(signedOut);
    await open();

    [...root().querySelectorAll<HTMLButtonElement>('[role="menuitem"]')]
      .find((item) => item.textContent?.includes('Sign out'))
      ?.click();

    expect(signedOut).toHaveBeenCalledTimes(1);
  });

  it('offers a workspace switch only when there is somewhere to switch to', async () => {
    await render('ada.owner@omniai.example', false);
    await open();
    expect(root().textContent).not.toContain('Switch workspace');

    root().remove();
    TestBed.resetTestingModule();

    await render('ada.owner@omniai.example', true);
    await open();
    expect(root().textContent).toContain('Switch workspace');
  });
});
