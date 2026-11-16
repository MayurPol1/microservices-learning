import { Injectable, computed, effect, signal } from '@angular/core';
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
 * that shows them when they change. It's saved to localStorage so a page refresh keeps the cart.
 */
@Injectable({ providedIn: 'root' })
export class CartStore {
  private readonly storageKey = 'microshop.cart';
  private readonly state = signal<CartItem[]>(this.load());

  readonly items = this.state.asReadonly();
  readonly count = computed(() => this.state().reduce((sum, item) => sum + item.quantity, 0));
  readonly total = computed(() => this.state().reduce((sum, item) => sum + item.price * item.quantity, 0));

  constructor() {
    // Runs whenever `state` changes.
    effect(() => this.save(this.state()));
  }

  add(product: Product, quantity = 1): void {
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

  private load(): CartItem[] {
    try {
      const saved = localStorage.getItem(this.storageKey);
      return saved ? (JSON.parse(saved) as CartItem[]) : [];
    } catch {
      return [];
    }
  }

  private save(items: CartItem[]): void {
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(items));
    } catch {
      // Storage can be unavailable (private mode); the cart then just isn't remembered.
    }
  }
}
