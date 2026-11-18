import { CurrencyPipe, DatePipe, DecimalPipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTableModule } from '@angular/material/table';
import { RouterLink } from '@angular/router';
import { Subscription, filter, switchMap, take, takeWhile, timer } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CustomerNotification, Order } from '../../core/models';
import { NotificationsApi, OrdersApi } from '../../core/orders-api';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog';

/**
 * Shows one order and watches it change. Right after checkout the order is "Pending"; this page
 * polls GET /api/orders/{id} once a second until Catalog's stock reservation (an event through
 * RabbitMQ) has made it "Confirmed" or "Rejected". That delay is eventual consistency, made visible.
 */
@Component({
  selector: 'app-order-detail',
  imports: [
    CurrencyPipe, DatePipe, DecimalPipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule,
    MatListModule, MatProgressBarModule, MatTableModule,
  ],
  templateUrl: './order-detail.html',
  styleUrl: './order-detail.scss',
})
export class OrderDetail implements OnInit {
  readonly id = input.required<string>();

  private readonly ordersApi = inject(OrdersApi);
  private readonly notificationsApi = inject(NotificationsApi);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  private static readonly maxPolls = 30;

  protected readonly order = signal<Order | null>(null);
  protected readonly notFound = signal(false);
  protected readonly stillPending = signal(false);
  protected readonly notifications = signal<CustomerNotification[]>([]);
  protected readonly cancelling = signal(false);

  /** Pending and Confirmed orders can be cancelled; Rejected and Cancelled ones can't. */
  protected readonly canCancel = computed(() => {
    const status = this.order()?.status;
    return status === 'Pending' || status === 'Confirmed';
  });
  protected readonly lineColumns = ['product', 'unitPrice', 'quantity', 'lineTotal'];

  /** Link to this order's trace, if we arrived here straight from checkout (see Cart.placeOrder). */
  protected readonly traceUrl: string | null;

  /** How long the asynchronous part took: from "placed" to "confirmed/rejected". */
  protected readonly secondsToComplete = computed(() => {
    const order = this.order();
    if (!order?.completedAtUtc) {
      return null;
    }
    return (Date.parse(order.completedAtUtc) - Date.parse(order.createdAtUtc)) / 1000;
  });

  private polling?: Subscription;

  constructor() {
    const traceId = (history.state as { traceId?: string | null } | null)?.traceId ?? null;
    this.traceUrl = traceId ? `${environment.dashboardUrl}/traces/detail/${traceId}` : null;
  }

  ngOnInit(): void {
    this.poll();
  }

  protected poll(): void {
    this.stillPending.set(false);
    this.polling?.unsubscribe();
    this.polling = timer(0, 1000)
      .pipe(
        take(OrderDetail.maxPolls),
        switchMap(() => this.ordersApi.get(this.id())),
        // Keep polling while Pending; `true` also emits the final (non-pending) value.
        takeWhile((order) => order.status === 'Pending', true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (order) => {
          this.order.set(order);
          if (order.status !== 'Pending') {
            this.loadNotifications(expectedEmails(order));
          }
        },
        error: () => {
          if (!this.order()) {
            this.notFound.set(true);
          }
        },
        complete: () => this.stillPending.set(this.order()?.status === 'Pending'),
      });
  }

  /**
   * Cancels the order (after a confirmation). The backend marks it Cancelled at once; Catalog gives the
   * stock back and Notifications sends an email a moment later, through the order.cancelled event.
   */
  protected cancel(): void {
    const data: ConfirmDialogData = {
      title: 'Cancel this order?',
      message: 'Any stock reserved for it goes back on sale, and you will get an email.',
      confirmText: 'Cancel order',
      cancelText: 'Keep order',
    };
    this.dialog
      .open(ConfirmDialog, { data })
      .afterClosed()
      .pipe(
        filter((confirmed) => confirmed === true),
        switchMap(() => {
          this.cancelling.set(true);
          this.polling?.unsubscribe();
          return this.ordersApi.cancel(this.id());
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (order) => {
          this.order.set(order);
          this.cancelling.set(false);
          this.loadNotifications(expectedEmails(order));
        },
        error: () => this.cancelling.set(false),
      });
  }

  // Emails arrive a moment after the order changes (events through RabbitMQ), so ask a few
  // times until all the expected ones are there.
  private loadNotifications(expected: number): void {
    timer(0, 1000)
      .pipe(
        take(8),
        switchMap(() => this.notificationsApi.forOrder(this.id())),
        takeWhile((list) => list.length < expected, true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((list) => this.notifications.set(list));
  }
}

/** How many emails an order ends up with: received + confirmed/rejected (+ cancelled). */
function expectedEmails(order: Order): number {
  if (order.status === 'Cancelled') {
    // Cancelled after it was confirmed: received, confirmed, cancelled. While pending: received, cancelled.
    return order.completedAtUtc ? 3 : 2;
  }
  return 2;
}
