import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Tenant } from '@app/core/api/tenant-api';
import { WorkspaceSwitcher, workspaceInitial } from './workspace-switcher';

const NORTHWIND: Tenant = { id: 'n-1', name: 'Northwind Operations', slug: 'nw', status: 'Active' };
const AL_NOOR: Tenant = { id: 'a-2', name: 'شركة النور', slug: 'alnoor', status: 'Active' };

/** The workspace switcher (Phase 39.3): a name, or a glass menu of workspaces. */
describe('WorkspaceSwitcher', () => {
  let fixture: ComponentFixture<WorkspaceSwitcher>;

  const root = (): HTMLElement => fixture?.nativeElement as HTMLElement;
  const trigger = () => root().querySelector<HTMLButtonElement>('[data-testid="workspace-switch"]');

  async function render(
    variant: 'panel' | 'rail' | 'bar',
    workspaces: readonly Tenant[],
    name = workspaces[0]?.name ?? '',
  ) {
    fixture = TestBed.createComponent(WorkspaceSwitcher);
    fixture.componentRef.setInput('variant', variant);
    fixture.componentRef.setInput('name', name);
    fixture.componentRef.setInput('currentId', workspaces[0]?.id ?? null);
    fixture.componentRef.setInput('workspaces', workspaces);
    document.body.appendChild(root());
    fixture.detectChanges();
    await fixture.whenStable();
  }

  afterEach(() => root()?.remove());

  it('takes a workspace’s own first letter as its mark, in any script', () => {
    expect(workspaceInitial(' northwind')).toBe('N');
    expect(workspaceInitial('شركة النور')).toBe('ش');
    expect(workspaceInitial('')).toBe('');
  });

  it('holds its place while the workspace loads, hidden from assistive technology', async () => {
    await render('panel', [], '');

    expect(root().querySelector('.switcher-static')?.getAttribute('aria-hidden')).toBe('true');
    expect(root().querySelector('.skeleton')).not.toBeNull();
  });

  it('is a name, not a control, with one workspace', async () => {
    await render('panel', [NORTHWIND]);

    expect(trigger()).toBeNull();
    expect(root().textContent?.replace(/\s+/g, ' ')).toContain('Workspace: Northwind Operations');
  });

  it('says the workspace and the action in the panel’s trigger', async () => {
    await render('panel', [NORTHWIND, AL_NOOR]);

    expect(trigger()?.getAttribute('aria-haspopup')).toBe('menu');
    expect(trigger()?.hasAttribute('aria-label')).toBe(false);
    expect(trigger()?.textContent?.replace(/\s+/g, ' ').trim()).toContain(
      'Workspace: Northwind Operations. Switch workspace',
    );
  });

  it('names the rail’s letter-only trigger in full', async () => {
    await render('rail', [NORTHWIND, AL_NOOR]);

    expect(trigger()?.getAttribute('aria-label')).toBe(
      'Workspace: Northwind Operations. Switch workspace',
    );
    expect(trigger()?.textContent?.trim()).toBe('N');
  });

  it('lists the workspaces with the current one checked, and the chooser after them', async () => {
    await render('bar', [NORTHWIND, AL_NOOR]);

    const chosen: Tenant[] = [];
    let chooser = 0;

    fixture.componentInstance.chosen.subscribe((tenant) => chosen.push(tenant));
    fixture.componentInstance.chooserRequested.subscribe(() => (chooser += 1));

    trigger()!.click();
    fixture.detectChanges();

    const options = [...root().querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]')];

    expect(
      options.map((option) => option.querySelector('.switcher-option-name')?.textContent?.trim()),
    ).toEqual(['Northwind Operations', 'شركة النور']);
    expect(options.map((option) => option.querySelector('.switcher-tile')?.textContent)).toEqual([
      'N',
      'ش',
    ]);
    expect(options[0].getAttribute('aria-checked')).toBe('true');

    // Choosing the current one changes nothing.
    options[0].click();
    expect(chosen).toEqual([]);

    trigger()!.click();
    fixture.detectChanges();
    root().querySelector<HTMLButtonElement>('[data-testid="workspace-chooser"]')!.click();
    expect(chooser).toBe(1);
  });
});
