import { Component, inject } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule, MatIconRegistry } from '@angular/material/icon';
import { MatToolbarModule } from '@angular/material/toolbar';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { environment } from '../environments/environment';
import { CartStore } from './core/cart-store';

/** The shell around every page: toolbar with navigation and the cart badge. */
@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, MatToolbarModule, MatButtonModule, MatIconModule, MatBadgeModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  protected readonly cart = inject(CartStore);
  protected readonly gatewayUrl = environment.apiBaseUrl;
  protected readonly dashboardUrl = environment.dashboardUrl;

  constructor() {
    // index.html loads the "Material Symbols Outlined" icon font; tell <mat-icon> to use it.
    inject(MatIconRegistry).setDefaultFontSetClass('material-symbols-outlined');
  }
}
