import { Component, inject } from '@angular/core';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';

/**
 * Asks how many units arrived. Stock is only ever *added to* (POST /api/products/{id}/restock),
 * never overwritten, so it can't undo a reservation an order made a moment earlier.
 */
@Component({
  selector: 'app-restock-dialog',
  imports: [ReactiveFormsModule, MatButtonModule, MatDialogModule, MatFormFieldModule, MatInputModule],
  template: `
    <h2 mat-dialog-title>Restock {{ data.name }}</h2>
    <mat-dialog-content>
      <p>Currently {{ data.availableStock }} in stock.</p>
      <mat-form-field appearance="outline">
        <mat-label>Units received</mat-label>
        <input matInput type="number" min="1" [formControl]="quantity" (keyup.enter)="confirm()" />
        @if (quantity.invalid) {
          <mat-error>Enter a whole number from 1 to 1,000,000.</mat-error>
        }
      </mat-form-field>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button mat-dialog-close>Cancel</button>
      <button mat-flat-button [disabled]="quantity.invalid" (click)="confirm()">Add stock</button>
    </mat-dialog-actions>
  `,
})
export class RestockDialog {
  protected readonly data = inject<{ name: string; availableStock: number }>(MAT_DIALOG_DATA);
  private readonly dialogRef = inject(MatDialogRef<RestockDialog, number>);

  protected readonly quantity = new FormControl(10, {
    nonNullable: true,
    validators: [Validators.required, Validators.min(1), Validators.max(1_000_000), Validators.pattern(/^\d+$/)],
  });

  protected confirm(): void {
    if (this.quantity.valid) {
      this.dialogRef.close(this.quantity.value);
    }
  }
}
