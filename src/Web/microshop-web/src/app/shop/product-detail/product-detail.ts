import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { CartStore } from '../../core/cart-store';
import { CatalogApi } from '../../core/catalog-api';
import { Product } from '../../core/models';

@Component({
  selector: 'app-product-detail',
  imports: [
    CurrencyPipe, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatProgressBarModule,
  ],
  templateUrl: './product-detail.html',
  styleUrl: './product-detail.scss',
})
export class ProductDetail implements OnInit {
  /** The :id route parameter (withComponentInputBinding in app.config.ts). */
  readonly id = input.required<string>();

  private readonly catalog = inject(CatalogApi);
  private readonly cart = inject(CartStore);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);

  protected readonly product = signal<Product | null>(null);
  protected readonly notFound = signal(false);
  protected readonly quantity = signal(1);

  ngOnInit(): void {
    this.catalog.get(this.id()).subscribe({
      next: (product) => this.product.set(product),
      error: () => this.notFound.set(true),
    });
  }

  protected addToCart(product: Product): void {
    const quantity = Math.floor(this.quantity());
    if (quantity < 1) {
      return;
    }
    this.cart.add(product, quantity);
    this.snackBar
      .open(`${quantity} x ${product.name} added to the cart`, 'View cart', { duration: 4000 })
      .onAction()
      .subscribe(() => this.router.navigate(['/cart']));
  }
}
