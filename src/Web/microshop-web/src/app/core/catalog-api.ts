import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { CreateProduct, PagedResult, Product, ProductSearch, SaveProduct } from './models';

/**
 * Calls the Catalog service. Note the URL: the API gateway (environment.apiBaseUrl), never
 * Catalog's own port. The web app only knows the front door; YARP routes /api/products to Catalog.
 */
@Injectable({ providedIn: 'root' })
export class CatalogApi {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiBaseUrl}/api/products`;

  search(search: ProductSearch): Observable<PagedResult<Product>> {
    let params = new HttpParams().set('page', search.page).set('pageSize', search.pageSize);
    if (search.category) {
      params = params.set('category', search.category);
    }
    if (search.q) {
      params = params.set('q', search.q);
    }
    return this.http.get<PagedResult<Product>>(`${this.url}/search`, { params });
  }

  categories(): Observable<string[]> {
    return this.http.get<string[]>(`${this.url}/categories`);
  }

  /** Every product (admin table). */
  getAll(): Observable<Product[]> {
    return this.http.get<Product[]>(this.url);
  }

  get(id: string): Observable<Product> {
    return this.http.get<Product>(`${this.url}/${id}`);
  }

  create(product: CreateProduct): Observable<Product> {
    return this.http.post<Product>(this.url, product);
  }

  update(id: string, product: SaveProduct): Observable<Product> {
    return this.http.put<Product>(`${this.url}/${id}`, product);
  }

  restock(id: string, quantity: number): Observable<Product> {
    return this.http.post<Product>(`${this.url}/${id}/restock`, { quantity });
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
