import { CurrencyPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterLink } from '@angular/router';
import { filter, switchMap } from 'rxjs';
import { CatalogApi } from '../../core/catalog-api';
import { Product } from '../../core/models';
import { ConfirmDialog, ConfirmDialogData } from '../../shared/confirm-dialog';
import { RestockDialog } from './restock-dialog';

/** Product administration: every product, with edit, restock and delete. */
@Component({
  selector: 'app-admin-products',
  imports: [CurrencyPipe, RouterLink, MatButtonModule, MatIconModule, MatProgressBarModule, MatTableModule, MatTooltipModule],
  templateUrl: './admin-products.html',
  styleUrl: './admin-products.scss',
})
export class AdminProducts implements OnInit {
  private readonly catalog = inject(CatalogApi);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);

  protected readonly products = signal<Product[] | null>(null);
  protected readonly columns = ['name', 'category', 'price', 'stock', 'actions'];

  ngOnInit(): void {
    this.load();
  }

  protected restock(product: Product): void {
    this.dialog
      .open(RestockDialog, { data: { name: product.name, availableStock: product.availableStock } })
      .afterClosed()
      .pipe(
        filter((quantity): quantity is number => typeof quantity === 'number'),
        switchMap((quantity) => this.catalog.restock(product.id, quantity)),
      )
      .subscribe((updated) => {
        this.snackBar.open(`${updated.name}: ${updated.availableStock} in stock now`, 'OK', { duration: 3000 });
        this.load();
      });
  }

  protected delete(product: Product): void {
    const data: ConfirmDialogData = {
      title: `Delete ${product.name}?`,
      message: 'Existing orders keep their copy of its name and price. New orders for it will be rejected.',
      confirmText: 'Delete',
    };
    this.dialog
      .open(ConfirmDialog, { data })
      .afterClosed()
      .pipe(
        filter((confirmed) => confirmed === true),
        switchMap(() => this.catalog.delete(product.id)),
      )
      .subscribe(() => {
        this.snackBar.open(`${product.name} deleted`, 'OK', { duration: 3000 });
        this.load();
      });
  }

  private load(): void {
    this.catalog.getAll().subscribe((products) => this.products.set(products));
  }
}
