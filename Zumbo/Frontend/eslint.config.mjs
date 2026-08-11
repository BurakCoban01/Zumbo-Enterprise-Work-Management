const browserGlobals = {
  angular: 'readonly',
  Blob: 'readonly',
  caches: 'readonly',
  clearTimeout: 'readonly',
  console: 'readonly',
  document: 'readonly',
  fetch: 'readonly',
  FormData: 'readonly',
  Intl: 'readonly',
  localStorage: 'readonly',
  location: 'readonly',
  lucide: 'readonly',
  navigator: 'readonly',
  Notification: 'readonly',
  Promise: 'readonly',
  self: 'readonly',
  sessionStorage: 'readonly',
  setTimeout: 'readonly',
  signalR: 'readonly',
  URL: 'readonly',
  URLSearchParams: 'readonly',
  window: 'readonly'
};

export default [
  {
    ignores: ['node_modules/**', 'vendor/**', 'dist/**', 'dist-modern/**', '.angular/**', 'frontend-run*.log', 'http*.log']
  },
  {
    files: ['desktop-bulma/**/*.js', 'mobile-ionic/**/*.js', 'shared/**/*.js'],
    languageOptions: {
      ecmaVersion: 2022,
      sourceType: 'script',
      globals: browserGlobals
    },
    rules: {
      'no-dupe-keys': 'error',
      'no-undef': 'error',
      'no-unreachable': 'error',
      'valid-typeof': 'error'
    }
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
