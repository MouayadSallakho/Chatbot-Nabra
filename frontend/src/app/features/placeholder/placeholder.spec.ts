import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Placeholder } from './placeholder';

/** An area with no screens yet: said plainly, with one way back. */
describe('Placeholder', () => {
  function render(area?: string): HTMLElement {
    TestBed.configureTestingModule({ imports: [Placeholder], providers: [provideRouter([])] });
    const fixture = TestBed.createComponent(Placeholder);
    if (area) {
      fixture.componentRef.setInput('area', area);
    }
    fixture.detectChanges();

    return fixture.nativeElement as HTMLElement;
  }

  it('names the area and says it is not in this build', () => {
    const root = render('Integrations');

    expect(root.querySelector('h1')?.textContent).toContain('Integrations');
    expect(root.textContent).toContain(
      'Integrations is part of the planned product and is not available in this build.',
    );
    expect(root.textContent).toContain('Not built yet');
  });

  it('falls back to a neutral name', () => {
    expect(render().querySelector('h1')?.textContent).toContain('This area');
  });

  it('offers exactly one way back, to the overview', () => {
    const links = render('Integrations').querySelectorAll('a');

    expect(links).toHaveLength(1);
    expect(links[0].getAttribute('href')).toBe('/overview');
  });
});
