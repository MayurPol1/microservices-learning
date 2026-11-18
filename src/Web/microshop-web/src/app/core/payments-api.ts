import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { Payment, StartCheckoutResponse } from './models';

/**
 * Calls the Payments service (through the gateway). The browser never sees a card number or a Stripe
 * key: it only gets a URL to Stripe's own payment page, and goes there.
 */
@Injectable({ providedIn: 'root' })
export class PaymentsApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/api/payments`;

  /** Only the order id: Payments asks Orders for the amount, so the browser can't change it. */
  startCheckout(orderId: string): Observable<StartCheckoutResponse> {
    return this.http.post<StartCheckoutResponse>(`${this.url}/checkout`, { orderId });
  }

  forOrder(orderId: string): Observable<Payment[]> {
    return this.http.get<Payment[]>(this.url, { params: { orderId } });
  }

  /** Back from Stripe: ask Payments to check the session with Stripe now, instead of waiting for the webhook. */
  refresh(sessionId: string): Observable<Payment> {
    return this.http.post<Payment>(`${this.url}/sessions/${encodeURIComponent(sessionId)}/refresh`, null);
  }
}
