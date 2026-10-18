// @ts-expect-error No Node type declarations are installed in this workspace.
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { AppTitleStrategy } from '@app/core/title/app-title-strategy';
import { PRODUCT_NAME } from './brand';

@Component({ template: '' })
class Blank {}

/** Text without HTML comments, which are for developers and never rendered. */
const withoutComments = (html: string): string => html.replace(/<!--[\s\S]*?-->/g, '');

/** Every component template under a folder. */
function templates(dir: string): string[] {
  return readdirSync(dir).flatMap((entry: string) => {
    const path = `${dir}/${entry}`;

    return statSync(path).isDirectory() ? templates(path) : path.endsWith('.html') ? [path] : [];
  });
}

/**
 * The public name is Nabra (نبرة); OmniAI is the internal code name and never
 * reaches a person using the product (docs/design/MASTER.md, section 0).
 *
 * The guards read the sources a reader's text comes from -- the catalogues,
 * the templates, index.html -- so a new "OmniAI" in copy fails here, while code
 * names in identifiers, storage keys and comments stay free to keep it.
 */
describe('the product name', () => {
  it('is Nabra in English', () => {
    expect(PRODUCT_NAME).toBe('Nabra');
  });

  it('is نبرة in the Arabic catalogue', () => {
    const arabic = JSON.parse(readFileSync('src/locale/messages.ar.json', 'utf8'));

    expect(arabic.translations['brand.name']).toBe('نبرة');
  });

  it('never appears as "OmniAI" in a message, in either language', () => {
    for (const file of ['src/locale/messages.json', 'src/locale/messages.ar.json']) {
      const messages: Record<string, string> = JSON.parse(readFileSync(file, 'utf8')).translations;
      const leaks = Object.entries(messages).filter(([, text]) => /omniai/i.test(text));

      expect(leaks, file).toEqual([]);
    }
  });

  it('never appears as "OmniAI" in a template', () => {
    const leaks = templates('src/app').filter((file) =>
      /omniai/i.test(withoutComments(readFileSync(file, 'utf8'))),
    );

    expect(leaks).toEqual([]);
  });

  it('is what index.html shows before the application starts', () => {
    const html = withoutComments(readFileSync('src/index.html', 'utf8'));
    const body = html.slice(html.indexOf('<body'));

    expect(html).toContain('<title>Nabra</title>');
    expect(body).not.toMatch(/omniai/i);
    expect(body).toMatch(/lang="en"[^>]*>\s*Nabra\s*</);
    expect(body).toMatch(/lang="ar"[^>]*>\s*نبرة\s*</);
  });

  it('points the browser at versioned icon files that exist', () => {
    const html = readFileSync('src/index.html', 'utf8');
    const icons = [...html.matchAll(/<link rel="icon"[^>]*href="([^"]+)"/g)].map(
      (match) => match[1],
    );

    expect(icons).toEqual(['nabra-icon.v1.svg', 'nabra-icon.v1.ico']);

    for (const icon of icons) {
      expect(existsSync(`public/${icon}`), icon).toBe(true);
    }

    // The old names are gone: the edge caches them for a year.
    expect(existsSync('public/favicon.svg')).toBe(false);
    expect(existsSync('public/favicon.ico')).toBe(false);
  });

  describe('in the browser tab', () => {
    beforeEach(() => {
      TestBed.configureTestingModule({
        providers: [
          provideRouter([
            { path: 'overview', title: 'Overview', component: Blank },
            { path: 'untitled', component: Blank },
          ]),
          { provide: TitleStrategy, useClass: AppTitleStrategy },
        ],
      });
    });

    it('follows the page title: "<page> · Nabra"', async () => {
      await TestBed.inject(Router).navigateByUrl('/overview');

      expect(TestBed.inject(Title).getTitle()).toBe('Overview · Nabra');
    });

    it('names the product alone on a page without a title', async () => {
      await TestBed.inject(Router).navigateByUrl('/untitled');

      expect(TestBed.inject(Title).getTitle()).toBe('Nabra Admin');
    });
  });
});
