// TypeScript shapes of the JSON the backend returns. They mirror the C# DTOs:
// Catalog's ProductDto/PagedResult, Orders' OrderDto, Notifications' Notification, Payments' PaymentDto.
// ASP.NET Core serialises property names in camelCase, hence `availableStock` for AvailableStock.

export interface Product {
  id: string;
  name: string;
  description: string | null;
  price: number;
  availableStock: number;
  category: string;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ProductSearch {
  category?: string;
  q?: string;
  page: number;
  pageSize: number;
}

/** Body of PUT /api/products/{id}. */
export interface SaveProduct {
  name: string;
  description: string | null;
  price: number;
  category: string;
}

/** Body of POST /api/products. */
export interface CreateProduct extends SaveProduct {
  initialStock: number;
}

export type OrderStatus = 'Pending' | 'Confirmed' | 'Rejected' | 'Paid' | 'Cancelled';

export interface OrderLine {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  lineTotal: number;
}

export interface Order {
  id: string;
  customerId: string;
  customerEmail: string;
  status: OrderStatus;
  total: number;
  rejectionReason: string | null;
  createdAtUtc: string;
  completedAtUtc: string | null;
  cancelledAtUtc: string | null;
  paidAtUtc: string | null;
  lines: OrderLine[];
}

export type PaymentStatus = 'Open' | 'Succeeded' | 'Expired' | 'Failed' | 'Cancelled' | 'Refunded';

/** One attempt to pay for an order, from the Payments service. */
export interface Payment {
  id: string;
  orderId: string;
  status: PaymentStatus;
  amount: number;
  currency: string;
  createdAtUtc: string;
  expiresAtUtc: string;
  paidAtUtc: string | null;
  refundedAtUtc: string | null;
  closedAtUtc: string | null;
}

/** What POST /api/payments/checkout returns: the Stripe page to send the browser to. */
export interface StartCheckoutResponse {
  paymentId: string;
  checkoutUrl: string;
  expiresAtUtc: string;
}

/** Only the items: who is ordering comes from the JWT, not from the request body. */
export interface PlaceOrderRequest {
  items: { productId: string; quantity: number }[];
}

/** An order accepted by the backend (202), plus the trace id of the request that placed it. */
export interface PlacedOrder {
  order: Order;
  traceId: string | null;
}

export interface CustomerNotification {
  id: string;
  orderId: string;
  to: string;
  subject: string;
  body: string;
  sentAtUtc: string;
}

/** RFC 9457 error body that every MicroShop service returns. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export type Role = 'Admin' | 'Customer';

export interface AuthUser {
  id: string;
  email: string;
  role: Role;
}

/** What POST /api/auth/login and /register return. */
export interface AuthResponse {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthUser;
}
