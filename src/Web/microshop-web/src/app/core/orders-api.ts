import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../environments/environment';
import { CustomerNotification, Order, PlaceOrderRequest, PlacedOrder } from './models';

/** Calls the Orders service (through the gateway). */
@Injectable({ providedIn: 'root' })
export class OrdersApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/api/orders`;

  /**
   * POST /api/orders answers 202 Accepted with status "Pending": stock is reserved later,
   * asynchronously. `observe: 'response'` gives us the headers too, so we can keep the
   * X-Trace-Id (exposed by the gateway's CORS policy) and link the order to its trace.
   */
  place(request: PlaceOrderRequest): Observable<PlacedOrder> {
    return this.http
      .post<Order>(this.url, request, { observe: 'response' })
      .pipe(map((response) => ({ order: response.body!, traceId: response.headers.get('X-Trace-Id') })));
  }

  list(): Observable<Order[]> {
    return this.http.get<Order[]>(this.url);
  }

  get(id: string): Observable<Order> {
    return this.http.get<Order>(`${this.url}/${id}`);
  }
}

/** Calls the Notifications service (through the gateway). */
@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/api/notifications`;

  forOrder(orderId: string): Observable<CustomerNotification[]> {
    return this.http.get<CustomerNotification[]>(this.url, { params: { orderId } });
  }
}
