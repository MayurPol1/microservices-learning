import { CurrencyPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/auth-store';
import { CartStore } from '../../core/cart-store';
import { OrdersApi } from '../../core/orders-api';

/**
 * The cart and checkout, for logged-in users only (authGuard in app.routes.ts): the cart belongs to
 * the user, and so does the order (Orders takes the owner from the JWT, not from a form field).
 * "Place order" sends ONE request to the gateway; everything after that happens in the backend.
 */
@Component({
  selector: 'app-cart',
  imports: [CurrencyPipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatTableModule],
  templateUrl: './cart.html',
  styleUrl: './cart.scss',
})
export class Cart {
  protected readonly cart = inject(CartStore);
  protected readonly auth = inject(AuthStore);
  private readonly orders = inject(OrdersApi);
  private readonly router = inject(Router);

  protected readonly columns = ['product', 'price', 'quantity', 'lineTotal', 'remove'];
  protected readonly placing = signal(false);

  protected changeQuantity(productId: string, value: string): void {
    this.cart.setQuantity(productId, Number(value));
  }

  protected placeOrder(): void {
    if (this.cart.items().length === 0 || !this.auth.isLoggedIn()) {
      return;
    }

    this.placing.set(true);
    const request = { items: this.cart.items().map((i) => ({ productId: i.productId, quantity: i.quantity })) };

    this.orders.place(request).subscribe({
      next: ({ order, traceId }) => {
        this.cart.clear();
        // Hand the trace id to the order page (it isn't stored anywhere in the backend).
        this.router.navigate(['/orders', order.id], { state: { traceId } });
      },
      // The error interceptor already showed the reason (e.g. 503 "Catalog unavailable").
      error: () => this.placing.set(false),
    });
  }
}
