// @ts-expect-error No Node type declarations are installed in this workspace.
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter } from '@angular/router';
import { AppTitleStrategy } from '@app/core/title/app-title-strategy';
import { PRODUCT_NAME } from './brand';
import { BRAND_MARK, BRAND_NAMES } from '@app/shared/brand-mark/brand-geometry';

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
    // The lockup (39.2b): the two-circle mark and both names as outlined
    // paths, the same geometry as BrandLockup, so it needs no font.
    expect(body).toContain(BRAND_MARK.lens);
    expect(body).toContain(BRAND_NAMES.latin.path);
    expect(body).toContain(BRAND_NAMES.arabic.path);
    // On Arabic pages the Arabic name leads.
    expect(html).toMatch(/html\[lang='ar'\] \.boot-name-latin \{\s*order: 1;/);
  });

  it('points the browser at versioned icon files that exist', () => {
    const html = readFileSync('src/index.html', 'utf8');
    const icons = [...html.matchAll(/<link rel="icon"[^>]*href="([^"]+)"/g)].map(
      (match) => match[1],
    );

    expect(icons).toEqual([
      'nabra-icon.v2.ico',
      'nabra-icon.v2-16.png',
      'nabra-icon.v2-32.png',
      'nabra-icon.v2-48.png',
      'nabra-icon.v2.svg',
    ]);

    for (const icon of icons) {
      expect(existsSync(`public/${icon}`), icon).toBe(true);
    }

    // The old names are gone: the edge caches them for a year.
    expect(existsSync('public/favicon.svg')).toBe(false);
    expect(existsSync('public/favicon.ico')).toBe(false);
    expect(existsSync('public/nabra-icon.v1.svg')).toBe(false);
    expect(existsSync('public/nabra-icon.v1.ico')).toBe(false);
  });

  it('draws the favicon as the two-circle mark, following the colour scheme (Phase 39.8a)', () => {
    const svg = readFileSync('public/nabra-icon.v2.svg', 'utf8');

    expect(svg).toContain(BRAND_MARK.lens);
    expect(svg).toContain(
      `cx="${BRAND_MARK.ai.cx}" cy="${BRAND_MARK.ai.cy}" r="${BRAND_MARK.ai.r}"`,
    );
    expect(svg).toContain(
      `cx="${BRAND_MARK.team.cx}" cy="${BRAND_MARK.team.cy}" r="${BRAND_MARK.team.r}"`,
    );
    // Light-ground colours, and the dark-ground ones under a dark scheme (brand.md).
    expect(svg).toMatch(/\.ai \{ fill: #235347; \}/);
    expect(svg).toMatch(/prefers-color-scheme: dark\)[\s\S]*\.ai \{ fill: #daf1de; \}/);
    expect(svg).not.toMatch(/omniai|<image|href=/i);
  });

  it('names an Apple touch icon and a manifest whose icons all exist (Phase 39.8a)', () => {
    const html = readFileSync('src/index.html', 'utf8');
    const apple = /<link rel="apple-touch-icon" href="([^"]+)"/.exec(html)?.[1];
    const manifestHref = /<link rel="manifest" href="([^"]+)"/.exec(html)?.[1];

    expect(apple).toBe('nabra-app-icon.v2-180.png');
    expect(existsSync(`public/${apple}`)).toBe(true);
    expect(manifestHref).toBe('manifest.webmanifest');

    const manifest = JSON.parse(readFileSync(`public/${manifestHref}`, 'utf8'));

    // The public name only, and no link to a domain the product does not own yet.
    expect(manifest.name).toBe('Nabra');
    expect(manifest.short_name).toBe('Nabra');
    expect(JSON.stringify(manifest)).not.toMatch(/omniai|https?:/i);
    expect(manifest.start_url).toBe('/');
    expect(
      manifest.icons.map((icon: { sizes: string; purpose: string }) => [icon.sizes, icon.purpose]),
    ).toEqual([
      ['192x192', 'any'],
      ['512x512', 'any'],
      ['512x512', 'maskable'],
    ]);

    for (const icon of manifest.icons as { src: string }[]) {
      // Versioned like every other image: the edge caches PNGs for a year.
      expect(icon.src).toMatch(/^\/nabra-app-icon\.v\d+-/);
      expect(existsSync(`public${icon.src}`), icon.src).toBe(true);
    }
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
