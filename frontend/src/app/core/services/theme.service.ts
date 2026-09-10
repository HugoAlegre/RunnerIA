import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ThemeService {
  readonly theme = signal<'light' | 'dark'>('light');

  constructor() {
    try {
      const t = localStorage.getItem('sot-runner-theme');
      if (t === 'dark' || t === 'light') this.apply(t);
    } catch {
      /* ignore */
    }
  }

  apply(t: 'light' | 'dark'): void {
    this.theme.set(t);
    document.documentElement.setAttribute('data-theme', t);
    try {
      localStorage.setItem('sot-runner-theme', t);
    } catch {
      /* ignore */
    }
  }

  toggle(): void {
    this.apply(this.theme() === 'dark' ? 'light' : 'dark');
  }
}
