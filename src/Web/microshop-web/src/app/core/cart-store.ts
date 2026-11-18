import { Injectable, computed, effect, inject, linkedSignal } from '@angular/core';
import { AuthStore } from './auth-store';
import { Product } from './models';

export interface CartItem {
  productId: string;
  name: string;
  price: number;
  quantity: number;
  /** Stock when it was added; the real check happens in Catalog when the order is placed. */
  availableStock: number;
}

/**
 * The shopping cart lives only in the browser (there is no Cart service yet). It's a signal-based
 * store: components read `items()`, `count()` and `total()`, and Angular updates every place
 * that shows them when they change.
 * <p>
 * Each logged-in user has their own cart, saved in localStorage under their user id, so it survives a
 * reload and comes back after logging in again. Logged out there is no cart at all: shopping needs a
 * login, and the next person on the same browser never sees someone else's cart.
 * </p>
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private static readonly storagePrefix = 'microshop.cart.';

  private readonly auth = inject(AuthStore);
  private readonly userId = computed(() => this.auth.user()?.id ?? null);

  /**
   * A linkedSignal is writable like a signal, but resets itself whenever its source changes: here,
   * whenever someone logs in or out, the cart switches to that user's saved cart (or to empty).
   */
  private readonly state = linkedSignal<string | null, CartItem[]>({
    source: this.userId,
    computation: (userId) => (userId ? this.load(userId) : []),
  });

  readonly items = this.state.asReadonly();
  readonly count = computed(() => this.state().reduce((sum, item) => sum + item.quantity, 0));
  readonly total = computed(() => this.state().reduce((sum, item) => sum + item.price * item.quantity, 0));

  constructor() {
    // Runs whenever the cart (or the user) changes. Nothing is saved while logged out.
    effect(() => {
      const userId = this.userId();
      const items = this.state();
      if (userId) {
        this.save(userId, items);
      }
    });
    this.forgetAnonymousCart();
  }

  /** Returns false (and changes nothing) when nobody is logged in. */
  add(product: Product, quantity = 1): boolean {
    if (!this.userId()) {
      return false;
    }

    this.state.update((items) => {
      const existing = items.find((i) => i.productId === product.id);
      if (existing) {
        return items.map((i) => (i.productId === product.id ? { ...i, quantity: i.quantity + quantity } : i));
      }
      return [
        ...items,
        { productId: product.id, name: product.name, price: product.price, quantity, availableStock: product.availableStock },
      ];
    });
    return true;
  }

  setQuantity(productId: string, quantity: number): void {
    if (!Number.isInteger(quantity) || quantity < 1) {
      return;
    }
    this.state.update((items) => items.map((i) => (i.productId === productId ? { ...i, quantity } : i)));
  }

  remove(productId: string): void {
    this.state.update((items) => items.filter((i) => i.productId !== productId));
  }

  clear(): void {
    this.state.set([]);
  }

  private load(userId: string): CartItem[] {
    try {
      const saved = localStorage.getItem(CartStore.storagePrefix + userId);
      return saved ? (JSON.parse(saved) as CartItem[]) : [];
    } catch {
      return [];
    }
  }

  private save(userId: string, items: CartItem[]): void {
    try {
      localStorage.setItem(CartStore.storagePrefix + userId, JSON.stringify(items));
    } catch {
      // Storage can be unavailable (private mode); the cart then just isn't remembered.
    }
  }

  /** Earlier versions kept one cart for everybody under "microshop.cart"; drop it. */
  private forgetAnonymousCart(): void {
    try {
      localStorage.removeItem('microshop.cart');
    } catch {
      // storage unavailable: nothing to remove
    }
  }
}
