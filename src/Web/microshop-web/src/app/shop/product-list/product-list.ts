import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatPaginatorModule, PageEvent } from '@angular/material/paginator';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/auth-store';
import { CartStore } from '../../core/cart-store';
import { CatalogApi } from '../../core/catalog-api';
import { PagedResult, Product } from '../../core/models';

/**
 * The shop's home page. All state is held in signals; every filter change calls load(),
 * which asks Catalog (via the gateway) for exactly one page: GET /api/products/search.
 */
@Component({
  selector: 'app-product-list',
  imports: [
    CurrencyPipe, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatChipsModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatPaginatorModule, MatProgressBarModule,
  ],
  templateUrl: './product-list.html',
  styleUrl: './product-list.scss',
})
export class ProductList implements OnInit {
  private readonly catalog = inject(CatalogApi);
  private readonly cart = inject(CartStore);
  private readonly snackBar = inject(MatSnackBar);
  private readonly router = inject(Router);
  protected readonly auth = inject(AuthStore);

  protected readonly categories = signal<string[]>([]);
  protected readonly category = signal('');
  protected readonly searchText = signal('');
  protected readonly page = signal(1);
  protected readonly pageSize = signal(6);
  protected readonly result = signal<PagedResult<Product> | null>(null);
  protected readonly loading = signal(false);

  ngOnInit(): void {
    this.catalog.categories().subscribe((categories) => this.categories.set(categories));
    this.load();
  }

  protected selectCategory(category: string): void {
    this.category.set(category);
    this.page.set(1);
    this.load();
  }

  protected search(): void {
    this.page.set(1);
    this.load();
  }

  protected clearSearch(): void {
    this.searchText.set('');
    this.search();
  }

  protected changePage(event: PageEvent): void {
    this.page.set(event.pageIndex + 1);
    this.pageSize.set(event.pageSize);
    this.load();
  }

  protected addToCart(product: Product): void {
    if (this.cart.add(product)) {
      this.snackBar.open(`${product.name} added to the cart`, 'OK', { duration: 2500 });
    }
  }

  /** Where to come back to after logging in: this page, with its filters. */
  protected get currentUrl(): string {
    return this.router.url;
  }

  private load(): void {
    this.loading.set(true);
    this.catalog
      .search({ category: this.category(), q: this.searchText().trim(), page: this.page(), pageSize: this.pageSize() })
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.loading.set(false);
        },
        error: () => this.loading.set(false),
      });
  }
}
