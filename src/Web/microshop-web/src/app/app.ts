import { Component, computed, inject } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { environment } from '../environments/environment';
import { AuthStore } from './core/auth-store';
import { CartStore } from './core/cart-store';
import { ThemeMode, ThemeStore } from './core/theme-store';

/** The shell around every page: toolbar with navigation, the cart badge and the user menu. */
@Component({
  selector: 'app-root',
  imports: [
    RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule,
    MatBadgeModule, MatMenuModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly cart = inject(CartStore);
  protected readonly auth = inject(AuthStore);
  protected readonly theme = inject(ThemeStore);

  protected readonly themeOptions: { mode: ThemeMode; label: string; icon: string }[] = [
    { mode: 'light', label: 'Light', icon: 'light_mode' },
    { mode: 'dark', label: 'Dark', icon: 'dark_mode' },
    { mode: 'system', label: 'Match my system', icon: 'brightness_auto' },
  ];
  protected readonly currentTheme = computed(() => this.themeOptions.find((o) => o.mode === this.theme.mode())!);
  private readonly router = inject(Router);
  protected readonly gatewayUrl = environment.apiBaseUrl;
  protected readonly dashboardUrl = environment.dashboardUrl;

  constructor() {
    // index.html loads the "Material Symbols Outlined" icon font; tell <mat-icon> to use it.
    inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined');
  }

  protected logout(): void {
    this.auth.logout();
    this.router.navigate(['/']);
  }
}
