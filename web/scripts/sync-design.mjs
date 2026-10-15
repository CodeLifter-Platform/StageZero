// Copies the CodeLifter Design System pieces this app uses from the sibling Platform-Design
// checkout into src/design, byte for byte. Never edit those copies: change the design project,
// re-export it to Platform-Design, then run this again.
//
//   npm run design:sync     copy from ../../Platform-Design (or $PLATFORM_DESIGN)
//   npm run design:check    fail if any copy differs from the source (skips when it is absent)
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const target = resolve(here, '../src/design');
const source = resolve(process.env.PLATFORM_DESIGN ?? join(here, '../../../Platform-Design'));
const check = process.argv.includes('--check');

// Only what the screens use. fonts.css is left out on purpose: it loads Google Fonts, and
// StageZero serves its own copies of Inter and JetBrains Mono (src/theme/stagezero.css).
const COMPONENTS = [
  'forms/Button', 'forms/Input', 'forms/Select', 'forms/Checkbox', 'forms/Switch',
  'forms/Textarea', 'forms/Segmented',
  'data/Card', 'data/Badge', 'data/StatusDot', 'data/Tag', 'data/DataTable',
  'feedback/Dialog', 'feedback/Toast',
  'navigation/Tabs', 'navigation/Sidebar',
  'progress/ProgressBar',
];
const TOKENS = ['colors', 'typography', 'spacing', 'effects'];

const files = [
  ...COMPONENTS.flatMap((c) => [`components/${c}.jsx`, `components/${c}.d.ts`]),
  ...TOKENS.map((t) => `tokens/${t}.css`),
];

const digest = (path) => createHash('sha256').update(readFileSync(path)).digest('hex');

if (!existsSync(join(source, 'readme.md'))) {
  if (check) {
    console.log(`design:check skipped: no Platform-Design checkout at ${source}`);
    process.exit(0);
  }
  console.error(`No Platform-Design checkout at ${source}. Set PLATFORM_DESIGN to its path.`);
  process.exit(1);
}

const drifted = [];
for (const file of files) {
  const from = join(source, file);
  const to = join(target, file);
  if (!existsSync(from)) {
    console.error(`Platform-Design has no ${file}`);
    process.exit(1);
  }
  if (check) {
    if (!existsSync(to) || digest(from) !== digest(to)) drifted.push(file);
    continue;
  }
  mkdirSync(dirname(to), { recursive: true });
  writeFileSync(to, readFileSync(from));
}

if (check) {
  if (drifted.length) {
    console.error(`src/design differs from Platform-Design (run npm run design:sync):\n  ${drifted.join('\n  ')}`);
    process.exit(1);
  }
  console.log(`src/design matches Platform-Design (${files.length} files).`);
} else {
  let commit = 'unknown';
  try {
    commit = execFileSync('git', ['-C', source, 'rev-parse', '--short', 'HEAD'], { encoding: 'utf8' }).trim();
  } catch { /* not a git checkout */ }
  writeFileSync(join(target, 'SOURCE.md'), [
    '# Vendored from Platform-Design',
    '',
    `Copied verbatim from \`CodeLifter-Platform/Platform-Design\` at \`${commit}\` by`,
    '`npm run design:sync`. **Do not edit these files**: change the design project, re-export',
    'it to Platform-Design, then sync again. `npm run design:check` fails on any drift.',
    '',
    ...files.map((f) => `- \`${f}\``),
    '',
  ].join('\n'));
  console.log(`Copied ${files.length} files from ${source} (${commit}).`);
}
