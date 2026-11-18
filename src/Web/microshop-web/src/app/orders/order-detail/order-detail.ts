import { CurrencyPipe, DatePipe, DecimalPipe, UpperCasePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { Router, RouterLink } from '@angular/router';
import { Subscription, catchError, filter, forkJoin, of, switchMap, take, takeWhile, timer } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthStore } from '../../core/auth-store';
import { CustomerNotification, Order, Payment } from '../../core/models';
import { NotificationsApi, OrdersApi } from '../../core/orders-api';
import { PaymentsApi } from '../../core/payments-api';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog';

/**
 * Shows one order and watches it change. Right after checkout the order is "Pending"; this page
 * polls GET /api/orders/{id} once a second until Catalog's stock reservation (an event through
 * RabbitMQ) has made it "Confirmed" or "Rejected". That delay is eventual consistency, made visible.
 * <p>
 * A Confirmed order can be paid: "Pay" sends the browser to Stripe's hosted page. Stripe sends it back
 * here with ?payment=success&session_id=…, and the page waits for the order to become "Paid".
 * </p>
 */
@Component({
  selector: 'app-order-detail',
  imports: [
    CurrencyPipe, DatePipe, DecimalPipe, UpperCasePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule,
    MatListModule, MatProgressBarModule, MatTableModule,
  ],
  templateUrl: './order-detail.html',
  styleUrl: './order-detail.scss',
})
export class OrderDetail implements OnInit {
  readonly id = input.required<string>();

  /** Query parameters Stripe adds when it sends the customer back (bound by withComponentInputBinding). */
  readonly payment = input<string>();
  readonly sessionId = input<string>(undefined, { alias: 'session_id' });

  private readonly ordersApi = inject(OrdersApi);
  private readonly notificationsApi = inject(NotificationsApi);
  private readonly paymentsApi = inject(PaymentsApi);
  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

  private static readonly maxPolls = 30;

  protected readonly order = signal<Order | null>(null);
  protected readonly notFound = signal(false);
  protected readonly stillPending = signal(false);
  protected readonly notifications = signal<CustomerNotification[]>([]);
  protected readonly payments = signal<Payment[]>([]);
  protected readonly cancelling = signal(false);
  protected readonly paying = signal(false);

  /** True after Stripe sent us back with "success", until Orders has recorded the payment. */
  protected readonly awaitingPayment = signal(false);

  /** Pending, Confirmed and Paid orders can be cancelled (a paid one is refunded); Rejected and Cancelled can't. */
  protected readonly canCancel = computed(() => {
    const status = this.order()?.status;
    return status === 'Pending' || status === 'Confirmed' || status === 'Paid';
  });

  /** Only the customer who placed a Confirmed order pays for it (an admin looking at it doesn't). */
  protected readonly canPay = computed(() => {
    const order = this.order();
    return order?.status === 'Confirmed' && order.customerId === this.auth.user()?.id && !this.awaitingPayment();
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
  private sideData?: Subscription;

  /** When polling may stop: by default once the order leaves Pending; after paying, once it leaves Confirmed. */
  private isSettled: (order: Order) => boolean = (order) => order.status !== 'Pending';

  constructor() {
    const traceId = (history.state as { traceId?: string | null } | null)?.traceId ?? null;
    this.traceUrl = traceId ? `${environment.dashboardUrl}/traces/detail/${traceId}` : null;
  }

  ngOnInit(): void {
    const sessionId = this.sessionId();
    if (this.payment() === 'success' && sessionId) {
      this.returnedFromStripe(sessionId);
    } else {
      if (this.payment() === 'cancelled') {
        this.snackBar.open('Payment cancelled. You can pay later from this page.', 'OK', { duration: 5000 });
        this.clearPaymentQuery();
      }
      this.poll();
    }
  }

  protected poll(): void {
    this.stillPending.set(false);
    this.polling?.unsubscribe();
    this.polling = timer(0, 1000)
      .pipe(
        take(OrderDetail.maxPolls),
        switchMap(() => this.ordersApi.get(this.id())),
        // Keep polling until settled; `true` also emits the final (settled) value.
        takeWhile((order) => !this.isSettled(order), true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (order) => {
          this.order.set(order);
          if (this.isSettled(order)) {
            this.awaitingPayment.set(false);
            this.loadSideData(order);
          }
        },
        error: () => {
          if (!this.order()) {
            this.notFound.set(true);
          }
        },
        complete: () => {
          const order = this.order();
          this.stillPending.set(order !== null && !this.isSettled(order));
        },
      });
  }

  /** Asks Payments for a Stripe Checkout page and leaves the app for it. Stripe sends the customer back here. */
  protected pay(): void {
    this.paying.set(true);
    this.paymentsApi.startCheckout(this.id()).subscribe({
      next: (checkout) => window.location.assign(checkout.checkoutUrl),
      error: () => this.paying.set(false),
    });
  }

  /**
   * Cancels the order (after a confirmation). The backend marks it Cancelled at once; Catalog gives the
   * stock back, Payments refunds a paid order, and Notifications sends emails, all through the
   * order.cancelled event.
   */
  protected cancel(): void {
    const paid = this.order()?.status === 'Paid';
    const data: ConfirmDialogData = {
      title: 'Cancel this order?',
      message: paid
        ? 'You paid for this order, so the money goes back to your card (a Stripe refund). Its stock goes back on sale.'
        : 'Any stock reserved for it goes back on sale, and you will get an email.',
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
          this.loadSideData(order);
        },
        error: () => this.cancelling.set(false),
      });
  }

  /**
   * Back from Stripe with a paid session. Payments learns about it twice: from Stripe's webhook, and from
   * this "refresh" call, which asks Stripe directly (so it works even without webhooks). Either way Orders
   * hears payment.succeeded a moment later, so keep polling until the order is Paid.
   */
  private returnedFromStripe(sessionId: string): void {
    this.awaitingPayment.set(true);
    this.isSettled = (order) => order.status !== 'Pending' && order.status !== 'Confirmed';
    this.clearPaymentQuery();
    this.paymentsApi
      .refresh(sessionId)
      .pipe(
        catchError(() => of(null)), // the webhook may still do it; keep polling either way
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => this.poll());
  }

  /** Removes ?payment=…&session_id=… so a reload doesn't repeat the "back from Stripe" handling. */
  private clearPaymentQuery(): void {
    void this.router.navigate([], { queryParams: {}, replaceUrl: true });
  }

  // Emails and payment updates arrive a moment after the order changes (events through RabbitMQ), so
  // ask a few times until all the expected emails are there.
  private loadSideData(order: Order): void {
    const expected = expectedEmails(order);
    this.sideData?.unsubscribe();
    this.sideData = timer(0, 1000)
      .pipe(
        take(8),
        switchMap(() =>
          forkJoin([
            this.notificationsApi.forOrder(this.id()),
            this.paymentsApi.forOrder(this.id()).pipe(catchError(() => of<Payment[]>([]))),
          ]),
        ),
        takeWhile(([emails]) => emails.length < expected, true),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(([emails, payments]) => {
        this.notifications.set(emails);
        this.payments.set(payments);
      });
  }
}

/** How many emails an order ends up with, from what has happened to it so far. */
function expectedEmails(order: Order): number {
  let count = 1; // "We received your order"
  if (order.completedAtUtc) count++; // confirmed (stock reserved) or rejected
  if (order.paidAtUtc) count++; // payment received
  if (order.cancelledAtUtc) count++; // cancelled
  if (order.paidAtUtc && order.cancelledAtUtc) count++; // refund
  return count;
}
