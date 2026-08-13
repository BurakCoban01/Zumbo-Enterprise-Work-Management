import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

const THEME_KEY = 'zumbo.mobileTheme';

@Injectable({ providedIn: 'root' })
export class MobileThemeService {
  private readonly document = inject(DOCUMENT);
  readonly theme = signal<'light' | 'dark'>(this.initialTheme());

  constructor() {
    this.apply(this.theme());
  }

  toggle(): void {
    const theme = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(theme);
    localStorage.setItem(THEME_KEY, theme);
    this.apply(theme);
  }

  private initialTheme(): 'light' | 'dark' {
    const saved = localStorage.getItem(THEME_KEY);
    if (saved === 'light' || saved === 'dark') return saved;
    return globalThis.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }

  private apply(theme: 'light' | 'dark'): void {
    this.document.body.classList.toggle('theme-dark', theme === 'dark');
    this.document.body.classList.toggle('theme-light', theme === 'light');
  }
}
