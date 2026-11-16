import { CurrencyPipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { CartStore } from '../../core/cart-store';
import { OrdersApi } from '../../core/orders-api';

/**
 * The cart and checkout. "Place order" sends ONE request to the gateway (POST /api/orders).
 * Everything after that (Orders asking Catalog for prices, saving the order, the order.placed
 * event, stock reservation, notifications) happens inside the backend; see lesson 8.
 */
@Component({
  selector: 'app-cart',
  imports: [
    CurrencyPipe, ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatTableModule,
  ],
  templateUrl: './cart.html',
  styleUrl: './cart.scss',
})
export class Cart {
  protected readonly cart = inject(CartStore);
  private readonly orders = inject(OrdersApi);
  private readonly router = inject(Router);

  protected readonly columns = ['product', 'price', 'quantity', 'lineTotal', 'remove'];
  protected readonly email = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email] });
  protected readonly placing = signal(false);

  protected changeQuantity(productId: string, value: string): void {
    this.cart.setQuantity(productId, Number(value));
  }

  protected placeOrder(): void {
    if (this.email.invalid || this.cart.items().length === 0) {
      this.email.markAsTouched();
      return;
    }

    this.placing.set(true);
    const request = {
      customerEmail: this.email.value,
      items: this.cart.items().map((i) => ({ productId: i.productId, quantity: i.quantity })),
    };

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
