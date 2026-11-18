import { Routes } from '@angular/router';
import { adminGuard, authGuard } from './core/auth-guards';

// URL -> component. Every page is lazy-loaded (`loadComponent`): its code is downloaded
// only when you first open it, which keeps the initial download small. canActivate guards keep
// anonymous users out of the order pages and non-admins out of the admin pages.
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
    canActivate: [authGuard],
    title: 'Cart · MicroShop',
  },
  {
    path: 'login',
    loadComponent: () => import('./auth/login/login').then((m) => m.Login),
    title: 'Log in · MicroShop',
  },
  {
    path: 'register',
    loadComponent: () => import('./auth/register/register').then((m) => m.Register),
    title: 'Create account · MicroShop',
  },
  {
    path: 'orders',
    canActivate: [authGuard],
    loadComponent: () => import('./orders/order-list/order-list').then((m) => m.OrderList),
    title: 'Orders · MicroShop',
  },
  {
    path: 'orders/:id',
    canActivate: [authGuard],
    loadComponent: () => import('./orders/order-detail/order-detail').then((m) => m.OrderDetail),
    title: 'Order · MicroShop',
  },
  {
    path: 'admin/products',
    canActivate: [adminGuard],
    loadComponent: () => import('./admin/admin-products/admin-products').then((m) => m.AdminProducts),
    title: 'Admin · MicroShop',
  },
  {
    path: 'admin/products/new',
    canActivate: [adminGuard],
    loadComponent: () => import('./admin/product-form/product-form').then((m) => m.ProductForm),
    title: 'New product · MicroShop',
  },
  {
    path: 'admin/products/:id/edit',
    canActivate: [adminGuard],
    loadComponent: () => import('./admin/product-form/product-form').then((m) => m.ProductForm),
    title: 'Edit product · MicroShop',
  },
  { path: '**', redirectTo: '' },
];
