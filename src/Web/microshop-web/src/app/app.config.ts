import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth-interceptor';
import { errorInterceptor } from './core/error-interceptor';

// The app's "Program.cs": every app-wide service is registered here (dependency injection).
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: a route parameter like :id arrives as the component input `id`.
    provideRouter(routes, withComponentInputBinding()),
    // HttpClient. Every request passes through the interceptors in order: first the token is attached,
    // then errors (on the way back) are turned into readable messages.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor, errorInterceptor])),
  ],
};
