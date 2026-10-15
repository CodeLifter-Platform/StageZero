import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, relative } from 'node:path';
import { describe, expect, it } from 'vitest';

const web = join(__dirname, '..');
const files = (dir: string): string[] => readdirSync(dir).flatMap((name) => {
  const path = join(dir, name);
  return statSync(path).isDirectory() ? files(path) : [path];
});

describe('the web app', () => {
  it('loads nothing from another host: no CDN, font service or script kit', () => {
    // Each would learn about every visit and break rendering offline.
    const sources = [join(web, 'index.html'), ...files(join(web, 'src')).filter((f) => /\.(css|tsx?|jsx)$/.test(f) && !f.endsWith('.test.ts'))];
    const external = /(?:@import\s+url\(|<(?:link|script|img|iframe)\b[^>]*\b(?:href|src)\s*=\s*)["']?(?:https?:)?\/\//i;

    const offenders = sources.filter((f) => external.test(readFileSync(f, 'utf8'))).map((f) => relative(web, f));

    expect(offenders).toEqual([]);
  });

  it('names colours only in the token layer', () => {
    // Views use --cl-* tokens; src/theme/stagezero.css is the app's token dictionary port,
    // and src/design is the vendored design system.
    const hex = /#[0-9a-fA-F]{3,8}\b/;
    const offenders = files(join(web, 'src'))
      .filter((f) => /\.(css|tsx?)$/.test(f) && !f.includes(`${join('src', 'design')}`) && !f.endsWith(join('theme', 'stagezero.css')) && !f.endsWith('.test.ts'))
      .filter((f) => hex.test(readFileSync(f, 'utf8').replace(/&#x?[0-9a-fA-F]+;/g, '')))
      .map((f) => relative(web, f));

    expect(offenders).toEqual([]);
  });
});
