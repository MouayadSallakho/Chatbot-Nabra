import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { NotFound } from './not-found';

/** An address that matches nothing: calm, honest, and one way back. */
describe('NotFound', () => {
  async function render(url: string): Promise<HTMLElement> {
    TestBed.configureTestingModule({
      imports: [NotFound],
      providers: [provideRouter([{ path: '**', component: NotFound }])],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(NotFound);
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('says what happened and that nothing changed', async () => {
    const root = await render('/no-such-page');

    expect(root.querySelector('h1')?.textContent).toContain('Page not found');
    expect(root.textContent).toContain('Nothing in your workspace has changed');
    // An absence, not a failure: the state is not drawn as an error.
    expect(root.querySelector('.state-error')).toBeNull();
  });

  it('shows the path that matched nothing as a left-to-right island, without query or fragment', async () => {
    const root = await render('/reports/old-link?token=secret-value#section');
    const address = root.querySelector('.not-found-address code');

    expect(address?.textContent).toBe('/reports/old-link');
    expect(address?.getAttribute('dir')).toBe('ltr');
    expect(root.textContent).not.toContain('secret-value');
  });

  it('offers exactly one way back, to the overview', async () => {
    const root = await render('/no-such-page');
    const links = root.querySelectorAll('a');

    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('/overview');
    expect(links[0].classList).toContain('btn-primary');
  });
});
