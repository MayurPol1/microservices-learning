import { Routes } from '@angular/router';

// URL -> component. Every page is lazy-loaded (`loadComponent`): its code is downloaded
// only when you first open it, which keeps the initial download small.
export const routes: Routes = [
  {
    path: '',
    loadComponent: () => import('./shop/product-list/product-list').then((m) => m.ProductList),
    title: 'MicroShop',
  },
  {
    path: 'products/:id',
    loadComponent: () => import('./shop/product-detail/product-detail').then((m) => m.ProductDetail),
    title: 'Product · MicroShop',
  },
  {
    path: 'cart',
    loadComponent: () => import('./shop/cart/cart').then((m) => m.Cart),
    title: 'Cart · MicroShop',
  },
  {
    path: 'orders',
    loadComponent: () => import('./orders/order-list/order-list').then((m) => m.OrderList),
    title: 'Orders · MicroShop',
  },
  {
    path: 'orders/:id',
    loadComponent: () => import('./orders/order-detail/order-detail').then((m) => m.OrderDetail),
    title: 'Order · MicroShop',
  },
  {
    path: 'admin/products',
    loadComponent: () => import('./admin/admin-products/admin-products').then((m) => m.AdminProducts),
    title: 'Admin · MicroShop',
  },
  {
    path: 'admin/products/new',
    loadComponent: () => import('./admin/product-form/product-form').then((m) => m.ProductForm),
    title: 'New product · MicroShop',
  },
  {
    path: 'admin/products/:id/edit',
    loadComponent: () => import('./admin/product-form/product-form').then((m) => m.ProductForm),
    title: 'Edit product · MicroShop',
  },
  { path: '**', redirectTo: '' },
];
