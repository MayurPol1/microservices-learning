import { CurrencyPipe, DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { Order } from '../../core/models';
import { OrdersApi } from '../../core/orders-api';

/** The 50 most recent orders (GET /api/orders), newest first. */
@Component({
  selector: 'app-order-list',
  imports: [CurrencyPipe, DatePipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTableModule],
  templateUrl: './order-list.html',
  styleUrl: './order-list.scss',
})
export class OrderList implements OnInit {
  private readonly ordersApi = inject(OrdersApi);
  protected readonly router = inject(Router);

  protected readonly orders = signal<Order[] | null>(null);
  protected readonly columns = ['placed', 'order', 'customer', 'items', 'total', 'status'];

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.ordersApi.list().subscribe((orders) => this.orders.set(orders));
  }
}
