export default [
  {
    ignores: ['node_modules/**', 'vendor/**', 'dist/**', 'dist-modern/**', 'dist-modern-dev/**', '.angular/**', 'frontend-run*.log', 'http*.log']
  },
  {
    files: ['tests/**/*.mjs'],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: 'module',
      globals: {
        Buffer: 'readonly',
        caches: 'readonly',
        console: 'readonly',
        document: 'readonly',
        fetch: 'readonly',
        localStorage: 'readonly',
        location: 'readonly',
        navigator: 'readonly',
        process: 'readonly',
        sessionStorage: 'readonly',
        setTimeout: 'readonly',
        URL: 'readonly',
        window: 'readonly'
      }
    },
    rules: {
      'no-dupe-keys': 'error',
      'no-undef': 'error',
      'no-unreachable': 'error',
      'valid-typeof': 'error'
    }
  }
];
