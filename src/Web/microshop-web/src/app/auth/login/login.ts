import { Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../../core/auth-store';

/** Log in, then go back to the page that asked for it (?returnUrl=...). */
@Component({
  selector: 'app-login',
  imports: [ReactiveFormsModule, RouterLink, MatButtonModule, MatCardModule, MatFormFieldModule, MatInputModule],
  templateUrl: './login.html',
  styleUrl: '../auth.scss',
})
export class Login {
  /** Query parameter, bound by withComponentInputBinding. */
  readonly returnUrl = input<string>();

  private readonly auth = inject(AuthStore);
  private readonly router = inject(Router);

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });
  protected readonly busy = signal(false);

  /** Seeded demo accounts (see the README), shown so you can try both roles. */
  protected readonly demoAccounts = [
    { label: 'Admin', email: 'admin@microshop.local', password: 'Admin123!' },
    { label: 'Customer', email: 'customer@microshop.local', password: 'Customer123!' },
  ];

  protected useDemo(account: { email: string; password: string }): void {
    this.form.setValue({ email: account.email, password: account.password });
  }

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    const { email, password } = this.form.getRawValue();
    this.auth.login(email, password).subscribe({
      next: () => this.router.navigateByUrl(this.returnUrl() || '/'),
      // "Invalid email or password." is shown by the error interceptor.
      error: () => this.busy.set(false),
    });
  }
}
