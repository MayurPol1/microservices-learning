import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStore } from './auth-store';

/**
 * Route guards decide whether a page may open. Like the hidden buttons, they're for a good user
 * experience (no page full of 401 errors), not for security: the backend enforces the real rules.
 */

/** Logged-in users only; others go to the login page and come back afterwards. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  return auth.isLoggedIn() ? true : inject(Router).createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};

/** Admins only; customers go back to the shop, anonymous users to the login page. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthStore);
  const router = inject(Router);
  if (auth.isAdmin()) {
    return true;
  }
  return auth.isLoggedIn() ? router.createUrlTree(['/']) : router.createUrlTree(['/login'], { queryParams: { returnUrl: state.url } });
};
