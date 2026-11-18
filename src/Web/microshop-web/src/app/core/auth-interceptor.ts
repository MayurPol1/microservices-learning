import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthStore } from './auth-store';

/**
 * Adds "Authorization: Bearer <jwt>" to every call to the gateway while someone is logged in.
 * If the backend answers 401 (token expired or no longer valid), the session is dropped and the
 * user is sent to the login page, then back to where they were.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthStore);
  const router = inject(Router);

  const token = auth.token();
  const isApiCall = request.url.startsWith(environment.apiBaseUrl);
  const outgoing = token && isApiCall ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(outgoing).pipe(
    catchError((error: HttpErrorResponse) => {
      // A 401 from login itself just means "wrong password"; anything else means the session is over.
      const isLoginCall = request.url.endsWith('/api/auth/login');
      if (error.status === 401 && token && !isLoginCall) {
        auth.logout();
        router.navigate(['/login'], { queryParams: { returnUrl: router.url } });
      }
      return throwError(() => error);
    }),
  );
};
