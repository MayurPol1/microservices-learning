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

| URL | Page | Who | Backend calls (through the gateway) |
|---|---|---|---|
| `/` | Products: category chips, name search, paging | everyone | `GET /api/products/categories`, `GET /api/products/search` |
| `/products/:id` | Product details, add a quantity to the cart | everyone | `GET /api/products/{id}` |
| `/cart` | Cart (kept in the browser) and checkout | everyone; placing the order needs a login | `POST /api/orders` → 202 *Pending* |
| `/login`, `/register` | Log in (with buttons for the two demo accounts), create a customer account | everyone | `POST /api/auth/login`, `POST /api/auth/register` |
| `/orders` | "My orders" for a customer, "All orders" for the admin | logged in | `GET /api/orders` |
| `/orders/:id` | One order, polled every second until *Confirmed*/*Rejected*; cancel it; its emails; link to its trace | logged in | `GET /api/orders/{id}`, `POST /api/orders/{id}/cancel`, `GET /api/notifications?orderId=` |
| `/admin/products` | Product table: edit, restock, delete | admin | `GET /api/products`, `POST …/restock`, `DELETE …/{id}` |
| `/admin/products/new`, `/admin/products/:id/edit` | Product form (name, category, price, stock) | admin | `POST /api/products`, `PUT /api/products/{id}` |

Demo accounts: `admin@microshop.local` / `Admin123!` and `customer@microshop.local` / `Customer123!`
(seeded by the Identity service; see the root README).

## How it's organised

```
src/
├── environments/            apiBaseUrl (the gateway) and dashboardUrl
└── app/
    ├── app.config.ts        providers: router, HttpClient + auth and error interceptors  (the "Program.cs")
    ├── app.routes.ts        URL → page; every page is lazy-loaded; guards on orders and admin
    ├── app.ts / .html       shell: toolbar (links depend on the role), cart badge, user menu, <router-outlet>
    ├── core/                no UI: talks to the backend, holds shared state
    │   ├── models.ts        TypeScript shapes of the backend's JSON (mirror the C# DTOs)
    │   ├── catalog-api.ts   CatalogApi: products, categories, search, admin calls
    │   ├── orders-api.ts    OrdersApi (+ X-Trace-Id, cancel), NotificationsApi
    │   ├── cart-store.ts    CartStore: signals + localStorage
    │   ├── auth-store.ts    AuthStore: login/register/logout; user, token, isAdmin signals + localStorage
    │   ├── auth-interceptor.ts   adds "Authorization: Bearer <token>"; on 401, logs out and goes to /login
    │   ├── auth-guards.ts   authGuard (logged in) and adminGuard (role Admin) for the router
    │   └── error-interceptor.ts  every HTTP error → ProblemDetails message in a snack bar
    ├── auth/                login, register
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
  one readable message (validation `errors`, `detail`, or `title`). A 403 reads "admins only".

## How login works

1. `/login` posts the email and password to `POST /api/auth/login` (the Identity service, through
   the gateway) and gets back a JWT plus the user (`email`, `role`).
2. `AuthStore` keeps both in signals and in `localStorage` (`microshop.session`), so a page reload
   keeps you logged in. An expired session is dropped when the app starts.
3. `auth-interceptor.ts` adds `Authorization: Bearer <token>` to every call to the gateway. If a
   service still answers 401 (the token expired meanwhile), it logs out and opens `/login` with a
   `returnUrl`, so you land back where you were. That's how the cart's "Log in to place your
   order" brings you back to the cart, with the cart still full.
4. The guards in `auth-guards.ts` keep anonymous users out of `/orders` and customers out of
   `/admin`, and the toolbar only shows the links your role can use.

The guards and hidden links are **only for a pleasant UI**. Anyone can edit the JavaScript in their
browser, so the real protection is in the services: every API call is checked against the token
there (see section 4.12 of the root README). Keeping the token in `localStorage` is simple but
readable by any script on the page; production apps often keep it in an `HttpOnly` cookie or use a
backend-for-frontend instead.
