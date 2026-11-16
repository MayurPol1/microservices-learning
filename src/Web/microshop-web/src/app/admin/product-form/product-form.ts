import { Component, OnInit, computed, inject, input, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { CatalogApi } from '../../core/catalog-api';

/**
 * One form for "new product" (/admin/products/new) and "edit product" (/admin/products/:id/edit).
 * The validators mirror Catalog's DataAnnotations (lengths, ranges), so most mistakes are caught
 * here; anything that still gets through comes back as a 400 and is shown by the error interceptor.
 */
@Component({
  selector: 'app-product-form',
  imports: [
    ReactiveFormsModule, RouterLink, MatAutocompleteModule, MatButtonModule, MatCardModule,
    MatFormFieldModule, MatIconModule, MatInputModule, MatProgressBarModule,
  ],
  templateUrl: './product-form.html',
  styleUrl: './product-form.scss',
})
export class ProductForm implements OnInit {
  /** The :id route parameter; undefined on /admin/products/new. */
  readonly id = input<string>();
  protected readonly isEdit = computed(() => !!this.id());

  private readonly catalog = inject(CatalogApi);
  private readonly router = inject(Router);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.maxLength(200)]],
    description: ['', [Validators.maxLength(1000)]],
    price: [0, [Validators.required, Validators.min(0), Validators.max(1_000_000)]],
    category: ['', [Validators.required, Validators.maxLength(100)]],
    initialStock: [0, [Validators.required, Validators.min(0), Validators.max(1_000_000)]],
  });

  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  private readonly categories = signal<string[]>([]);
  private readonly categoryText = toSignal(this.form.controls.category.valueChanges, { initialValue: '' });

  /** Existing categories that match what's typed, for the autocomplete. */
  protected readonly categorySuggestions = computed(() => {
    const text = this.categoryText().toLowerCase();
    return this.categories().filter((c) => c.toLowerCase().includes(text));
  });

  ngOnInit(): void {
    this.catalog.categories().subscribe((categories) => this.categories.set(categories));

    const id = this.id();
    if (id) {
      // Stock is changed with "Restock", not edited: see Product.UpdateDetails in Catalog.
      this.form.controls.initialStock.disable();
      this.loading.set(true);
      this.catalog.get(id).subscribe({
        next: (p) => {
          this.form.patchValue({ name: p.name, description: p.description ?? '', price: p.price, category: p.category });
          this.loading.set(false);
        },
        error: () => this.router.navigate(['/admin/products']),
      });
    }
  }

  protected save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const value = this.form.getRawValue();
    const details = {
      name: value.name.trim(),
      description: value.description.trim() || null,
      price: Number(value.price),
      category: value.category.trim(),
    };

    const id = this.id();
    const request = id
      ? this.catalog.update(id, details)
      : this.catalog.create({ ...details, initialStock: Number(value.initialStock) });

    this.saving.set(true);
    request.subscribe({
      next: (product) => {
        this.snackBar.open(`${product.name} saved`, 'OK', { duration: 3000 });
        this.router.navigate(['/admin/products']);
      },
      error: () => this.saving.set(false),
    });
  }
}
