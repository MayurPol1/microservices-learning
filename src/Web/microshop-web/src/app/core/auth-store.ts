import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, AuthUser } from './models';

interface Session {
  accessToken: string;
  expiresAtUtc: string;
  user: AuthUser;
}

/**
 * Who is logged in. Login/register call the Identity service (through the gateway) and keep the
 * returned JWT; auth-interceptor.ts then sends it as "Authorization: Bearer …" on every API call.
 * <p>
 * The UI uses `isAdmin()` only to decide what to *show*. The real protection is in the backend:
 * each service checks the token's signature and role itself, so editing localStorage or the
 * JavaScript can hide buttons but never grants access.
 * </p>
 */
@Injectable({ providedIn: 'root' })
export class AuthStore {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/api/auth`;
  private readonly storageKey = 'microshop.session';

  private readonly session = signal<Session | null>(this.load());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.accessToken ?? null);
  readonly isLoggedIn = computed(() => this.session() !== null);
  readonly isAdmin = computed(() => this.session()?.user.role === 'Admin');

  login(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.url}/login`, { email, password }).pipe(tap((r) => this.start(r)));
  }

  register(email: string, password: string): Observable<AuthResponse> {
    return this.http.post<AuthResponse>(`${this.url}/register`, { email, password }).pipe(tap((r) => this.start(r)));
  }

  logout(): void {
    this.session.set(null);
    try {
      localStorage.removeItem(this.storageKey);
    } catch {
      // storage unavailable: nothing to remove
    }
  }

  private start(response: AuthResponse): void {
    const session: Session = { accessToken: response.accessToken, expiresAtUtc: response.expiresAtUtc, user: response.user };
    this.session.set(session);
    try {
      localStorage.setItem(this.storageKey, JSON.stringify(session));
    } catch {
      // storage unavailable: the session lasts until the page is reloaded
    }
  }

  /** Restores the session after a page reload, unless the token has expired. */
  private load(): Session | null {
    try {
      const saved = localStorage.getItem(this.storageKey);
      if (!saved) {
        return null;
      }
      const session = JSON.parse(saved) as Session;
      return Date.parse(session.expiresAtUtc) > Date.now() ? session : null;
    } catch {
      return null;
    }
  }
}
