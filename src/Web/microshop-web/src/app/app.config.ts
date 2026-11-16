import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { errorInterceptor } from './core/error-interceptor';

// The app's "Program.cs": every app-wide service is registered here (dependency injection).
export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // withComponentInputBinding: a route parameter like :id arrives as the component input `id`.
    provideRouter(routes, withComponentInputBinding()),
    // HttpClient, with every request passing through the error interceptor.
    provideHttpClient(withFetch(), withInterceptors([errorInterceptor])),
  ],
};
