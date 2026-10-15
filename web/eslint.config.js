import js from '@eslint/js';
import globals from 'globals';
import reactHooks from 'eslint-plugin-react-hooks';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  // src/design is vendored verbatim from Platform-Design: it is checked for drift
  // (npm run design:check), never edited, so it is not linted here.
  { ignores: ['dist', 'coverage', 'src/design/**'] },
  {
    files: ['**/*.{ts,tsx}'],
    extends: [js.configs.recommended, ...tseslint.configs.strict],
    languageOptions: { ecmaVersion: 2022, globals: globals.browser },
    plugins: { 'react-hooks': reactHooks },
    rules: {
      ...reactHooks.configs.recommended.rules,
      // Raw colours belong in src/theme/stagezero.css only (design tokens, never hex in views).
      'no-restricted-syntax': ['error', {
        selector: 'Literal[value=/#[0-9a-fA-F]{3,8}\\b/]',
        message: 'No raw hex in components: use a --cl-* token.',
      }],
    },
  },
  {
    files: ['scripts/**/*.mjs'],
    extends: [js.configs.recommended],
    languageOptions: { globals: globals.node },
  },
);
