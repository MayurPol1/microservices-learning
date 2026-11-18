import { DOCUMENT, Injectable, effect, inject, signal } from '@angular/core';

/** "system" follows the operating system's light/dark setting. */
export type ThemeMode = 'light' | 'dark' | 'system';

/**
 * Light, dark or "like my system". The colors themselves are in styles.scss: Angular Material's
 * variables use CSS light-dark(), so all this store does is set `color-scheme` on <html>. The choice is
 * remembered in localStorage; index.html applies it before Angular starts, so a dark page never
 * flashes white on load.
 */
@Injectable({ providedIn: 'root' })
export class ThemeStore {
  static readonly storageKey = 'microshop.theme';

  private readonly document = inject(DOCUMENT);

  readonly mode = signal<ThemeMode>(this.load());

  constructor() {
    effect(() => {
      const mode = this.mode();
      this.document.documentElement.style.colorScheme = mode === 'system' ? 'light dark' : mode;
      this.save(mode);
    });
  }

  set(mode: ThemeMode): void {
    this.mode.set(mode);
  }

  private load(): ThemeMode {
    try {
      const saved = localStorage.getItem(ThemeStore.storageKey);
      return saved === 'light' || saved === 'dark' ? saved : 'system';
    } catch {
      return 'system';
    }
  }

  private save(mode: ThemeMode): void {
    try {
      if (mode === 'system') {
        localStorage.removeItem(ThemeStore.storageKey);
      } else {
        localStorage.setItem(ThemeStore.storageKey, mode);
      }
    } catch {
      // storage unavailable: the choice lasts until the page is reloaded
    }
  }
}
