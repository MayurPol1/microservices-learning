# MicroShop web app (Angular)

The shop and admin front end for MicroShop. Angular 22 (standalone components, signals, zoneless)
with Angular Material. It talks to **one** address, the API gateway at `http://localhost:5000`,
and never to a service directly.

## Run it

Prerequisites: Node.js 22.22+, 24.15+ or 26+ (`node --version`), and the backend running
(`docker compose up -d` in the repository root).

```bash
npm install          # first time only: downloads the packages into node_modules
npm start            # = ng serve, then open http://localhost:4200
```

`npm run build` makes a production build in `dist/microshop-web/browser`.

## Pages

| URL | Page | Backend calls (through the gateway) |
|---|---|---|
| `/` | Products: category chips, name search, paging | `GET /api/products/categories`, `GET /api/products/search` |
| `/products/:id` | Product details, add a quantity to the cart | `GET /api/products/{id}` |
| `/cart` | Cart (kept in the browser) and checkout | `POST /api/orders` → 202 *Pending* |
| `/orders` | Latest orders | `GET /api/orders` |
| `/orders/:id` | One order, polled every second until *Confirmed*/*Rejected*; its emails; link to its trace | `GET /api/orders/{id}`, `GET /api/notifications?orderId=` |
| `/admin/products` | Product table: edit, restock, delete | `GET /api/products`, `POST …/restock`, `DELETE …/{id}` |
| `/admin/products/new`, `/admin/products/:id/edit` | Product form | `POST /api/products`, `PUT /api/products/{id}` |

## How it's organised

```
src/
├── environments/            apiBaseUrl (the gateway) and dashboardUrl
└── app/
    ├── app.config.ts        providers: router, HttpClient + error interceptor  (the "Program.cs")
    ├── app.routes.ts        URL → page; every page is lazy-loaded
    ├── app.ts / .html       shell: toolbar, cart badge, <router-outlet>
    ├── core/                no UI: talks to the backend, holds shared state
    │   ├── models.ts        TypeScript shapes of the backend's JSON (mirror the C# DTOs)
    │   ├── catalog-api.ts   CatalogApi: products, categories, search, admin calls
    │   ├── orders-api.ts    OrdersApi (+ X-Trace-Id), NotificationsApi
    │   ├── cart-store.ts    CartStore: signals + localStorage
    │   └── error-interceptor.ts  every HTTP error → ProblemDetails message in a snack bar
    ├── shop/                product-list, product-detail, cart
    ├── orders/              order-list, order-detail
    ├── admin/               admin-products (+ restock dialog), product-form
    └── shared/              confirm-dialog
```

Files follow Angular's 2025 naming style: `product-list.ts` holds the class `ProductList`
(no `.component` suffix).

## How it connects to the backend

- **One address.** `environment.apiBaseUrl` is the gateway. YARP routes `/api/products` to Catalog,
  `/api/orders` to Orders, `/api/notifications` to Notifications.
- **CORS.** The page comes from `localhost:4200`, the API from `localhost:5000`: different origins,
  so the browser only lets the page read responses because the gateway allows that origin
  (`Cors:AllowedOrigins` in `src/ApiGateway/appsettings.json`). It also exposes `X-Trace-Id`,
  which the order page turns into a link to the Aspire Dashboard.
- **Errors.** Every service answers errors as ProblemDetails; `error-interceptor.ts` turns them into
  one readable message (validation `errors`, `detail`, or `title`).
