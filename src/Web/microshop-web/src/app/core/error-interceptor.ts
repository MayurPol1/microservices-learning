import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { ProblemDetails } from './models';

/**
 * Every HTTP call passes through this interceptor (like ASP.NET Core middleware, but on the client).
 * On an error it shows a message built from the ProblemDetails body the backend sends,
 * then re-throws so the calling component can react too (stop a spinner, for example).
 */
export const errorInterceptor: HttpInterceptorFn = (request, next) => {
  const snackBar = inject(MatSnackBar);
  return next(request).pipe(
    catchError((error: HttpErrorResponse) => {
      snackBar.open(describeError(error), 'Close', { duration: 8000 });
      return throwError(() => error);
    }),
  );
};

export function describeError(error: HttpErrorResponse): string {
  if (error.status === 0) {
    return `Can't reach the API gateway at ${environment.apiBaseUrl}. Is "docker compose up" running?`;
  }
  if (error.status === 429) {
    return 'Too many requests: the gateway rate limit was hit. Wait a few seconds.';
  }
  const problem = error.error as ProblemDetails | null;
  if (problem?.errors) {
    // Validation errors (400): { "Name": ["The Name field is required."], ... }
    return Object.values(problem.errors).flat().join(' ');
  }
  return problem?.detail ?? problem?.title ?? `${error.status} ${error.statusText}`;
}
