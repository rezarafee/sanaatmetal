# AGENTS.md — SanaatMetal (sanaatmetal.ir)

Guide for AI coding agents (Claude, Codex, Cursor, Copilot, …) and new developers.
Read this first; it is the single source of truth for how the project is built and the rules to keep.
`README.md` (Persian, features/roles) and `DEPLOY.md` (Persian, hosting step by step) have more detail for humans.

## 1. What the project is

Website + back-office for **Sanaat Metal**, an Iranian seller of industrial pipes, flanges, valves and fittings.

- **Public site** (`/`): bilingual **Persian (fa, RTL, default)** and **English (en, LTR)**, light/dark theme, mobile-first.
  Catalog with sizes/prices, certificates & datasheets, cart, order by mobile number, contact form.
- **Admin panel** (`/admin`): **Persian only, RTL**, sidebar on the right. Products, pricing (with history),
  site orders → pro-forma / invoices (cash / cheque / mixed, PDF, print, soft-delete trash), customers, client logos,
  messages, reports (Excel/PDF), settings (SMTP, offices, logo, admin font size), users, role/permission matrix, audit log.
- Owner: GitHub `rezarafee`. The owner writes in **Persian**; reply to them in Persian. UI text is Persian first.

## 2. Stack

| Part | Tech |
|---|---|
| Runtime | **.NET 10**, ASP.NET Core **MVC** (controllers + Razor views, no Razor Pages, no SPA) |
| Data | **EF Core 10**, **SQL Server** in production; **SQLite** for quick local runs (`Database:Provider=Sqlite`) |
| Auth | ASP.NET Core **Identity** (`AppUser : IdentityUser`) + custom permission policies |
| PDF / Excel | **QuestPDF** (Community license), **ClosedXML** |
| Mail | **MailKit** (SMTP from DB settings), Plesk XML API for mailboxes |
| Front-end | **Bootstrap 5.3** (RTL build for fa), Bootstrap Icons, Swiper, Chart.js, JalaliDatePicker — all **vendored in `wwwroot/lib`** (no npm, no CDN, no bundler) |
| JS | Plain ES2017+ vanilla JS: `wwwroot/js/site.js` (public), `wwwroot/js/admin.js` (admin), `theme-init.js` |
| Fonts | Vazirmatn (text), Lalezar (titles); optional `wwwroot/fonts/titr.woff2` overrides titles. PDF fonts in `Fonts/*.ttf` |
| Hosting | Windows shared hosting with **Plesk** (IIS, in-process), framework-dependent `win-x64` publish |

## 3. Folder layout

```
SanaatMetal.slnx            solution
dotnet-tools.json           dotnet-ef 10.0.12 (run `dotnet tool restore`)
README.md / DEPLOY.md       human docs (Persian)
src/SanaatMetal/
  Program.cs                ALL startup: logging, data-protection keys, DB, Identity, cookies, rate limits, localization, middleware, routes
  appsettings.json          defaults (no secrets). appsettings.Development.json = demo seed. appsettings.Production.json = host only, git-ignored
  web.config                IIS/Plesk (requestLimits 100MB; remove that block if host shows 500.19)
  Controllers/
    PublicControllers.cs    HomeController, ProductsController, CartController (attribute routes: /, /about, /contact, /products, /products/{slug}, /downloads, /cart, /api/cart, /lang/{code}, /error/{code})
    SeoController.cs        /robots.txt, /sitemap.xml (generated from DB, cached)
  Areas/Admin/
    Controllers/            AdminControllerBase + one file per area: Account, Catalog(Categories, Materials), Content(Clients, Messages, Settings, Audit),
                            Dashboard, Pricing, Products(+Documents), Reports, Sales(Invoices, Orders, Customers), Users(+Roles)
    Models/AdminPage.cs     base class of every admin view (Can(), N(), D() helpers)
    Views/                  one folder per controller; Shared/_AdminLayout.cshtml, _AuthLayout.cshtml (login), AdminError.cshtml
  Views/                    public views; Shared/_Layout.cshtml (SEO head, nav, cart drawer, footer, mobile bottom bar)
  ViewComponents/           NavCategories (menu)
  ViewModels/               public view models
  Models/                   Catalog.cs, Sales.cs, Content.cs  (entities; Entity base = Id, CreatedAt, UpdatedAt)
  Data/                     AppDbContext, SeedData, Migrations/
  Security/                 Roles, Permissions (matrix + defaults), policy provider/handler, SecurityHeadersMiddleware (CSP)
  Services/                 Lang (i18n), Formatting (Fmt: numbers, Jalali dates, digits), Seo, CoreServices (Settings, FileStorage, Email, Audit),
                            SalesServices (Pricing, Invoice), ExportService (PDF/Excel), PasswordResetService, PleskMailService
  Infrastructure/           culture provider, decimal binder, Persian Identity errors, file logger + WritableDir, /diag page, AdminErrorFilter, StoredUploads
  wwwroot/                  css/site.css (public), css/admin.css (admin), css/fonts.css, js/, lib/ (vendored), img/brand (logos), img/demo, uploads/ (git-ignored)
```

## 4. Build, run, migrate

Prerequisite: .NET SDK 10. (In a Linux sandbox: `apt-get install -y dotnet-sdk-10.0`; dot.net downloads may be blocked by proxies.)

```bash
dotnet build src/SanaatMetal

# Quick local run without SQL Server (demo data + user superadmin / Admin@123456 in Development):
cd src/SanaatMetal
ASPNETCORE_ENVIRONMENT=Development Database__Provider=Sqlite dotnet run --no-launch-profile --urls http://localhost:5294

# With SQL Server: set ConnectionStrings:DefaultConnection (env ConnectionStrings__DefaultConnection) and `dotnet run`.
```

- On startup `SeedData.InitializeAsync` runs **`Migrate()` on SQL Server** but **`EnsureCreated()` on SQLite**
  (so delete the `.db` file after model changes when using SQLite).
- First super admin comes from `Seed:AdminEmail` + `Seed:AdminPassword` (username `Seed:AdminUserName`, default `superadmin`) only when no users exist.
  `Seed:DemoData=true` adds sample catalog (Development only). Emergency reset: `Seed:RecoverSuperAdminPassword` (remove right after use).
- **After changing any entity**, add a migration (SQL Server is the target):
  ```bash
  dotnet tool restore
  dotnet ef migrations add NameOfChange -p src/SanaatMetal -o Data/Migrations
  ```
  Never edit an applied migration; production applies new ones automatically on restart.
- Razor views are compiled at build time (no runtime compilation): **rebuild + restart** to see `.cshtml` changes. CSS/JS in `wwwroot` are served live.
- There is **no test project**. Verify by building and by running the app (Playwright/Chromium works well for screenshots at 360/390/768/1024 px).

### Publish (what the owner deploys)
```bash
dotnet publish src/SanaatMetal -c Release -r win-x64 --self-contained false -o publish
```
The owner uploads `publish/` to Plesk `httpdocs`. Never overwrite `appsettings.Production.json` or `wwwroot/uploads` on the host.
Historically the deliverable was also zipped as `SanaatMetal.zip` (publish + source + Persian readme) in the project files.

## 5. Configuration keys (appsettings / env with `__`)

| Key | Meaning |
|---|---|
| `ConnectionStrings:DefaultConnection` | SQL Server. `ConnectionStrings:Sqlite` optional for SQLite |
| `Database:Provider` | `SqlServer` (default) or `Sqlite` |
| `Seed:*` | see §4 |
| `Site:BaseUrl` | `https://sanaatmetal.ir` on the host; used for canonical, sitemap and password-reset links |
| `Seo:GoogleVerification` | Search Console meta value |
| `DataProtection:KeysPath` | optional; otherwise first writable of `keys/`, `App_Data/keys`, `wwwroot/_private/keys`, else in-memory |
| `Diagnostics:LogPath`, `LogLevel`, `ShowDetailedErrors`, `DiagPage`, `DiagKey` | file log location/level, temp detailed errors, `/diag` page (super admin only, or `?key=`) |
| `Plesk:Url/ApiKey/Login/Password/Domain/AllowInvalidCertificate` | mailbox management; **only in appsettings.Production.json** |
| `Kestrel:Limits:MaxRequestBodySize` | 30 MB |

SMTP, company info, two offices, social links, currency, invoice footer, logo and `AdminFontSize` (12–18) live in the **`SiteSettings` DB row**
(edited in admin Settings, cached by `SettingsService`; SMTP password encrypted with Data Protection; SMTP section editable by super admin only).

## 6. Hosting realities (important — learned the hard way)

The host is **shared Windows/Plesk** and the app pool **cannot create folders or get write permission** in the site root. Therefore:
- Logs and data-protection keys use `WritableDir.Pick(...)` fallbacks, ending in `wwwroot/_private/...`, which `PrivateDataMiddleware` hides (404).
- Uploads: `FileStorageService` writes to `wwwroot/uploads` if possible, otherwise stores bytes in the **`StoredFiles` table**;
  `MapStoredUploads` serves them at the **same `/uploads/...` URL**. Always save/read uploads through `FileStorageService`, never `File.WriteAllBytes`.
- Errors: `logs/errors-yyyy-MM-dd.log` (or fallback dir); 500 page shows a reference code; `/diag` shows build stamp, writable dirs, recent errors.
  When the owner reports a host error, ask for the `/diag` text or the log file.
- Admin exceptions are caught by `AdminErrorFilter` → flash message + ref code inside the panel (`AdminError.cshtml`), not a raw 500.
- Do not add features that need: background services writing to disk, extra Windows services, Redis, Node at runtime, outbound internet (except Plesk API/SMTP).

## 7. Auth, roles and permissions

- Roles (`Security/Permissions.cs` → `Roles`): `SuperAdmin`, `Admin`, `SalesManager`, `Secretary` (حسابدار/منشی).
- Permissions are strings like `Catalog.Manage`, grouped in `Permissions.Groups` (shown in the /admin/roles matrix).
  Defaults per role in `Permissions.Defaults`, stored in table `RolePermissions` (seeded once, then edited by super admin). **SuperAdmin always has all.**
- Protect actions with `[HasPermission(Permissions.X)]` (policy `perm:X`, resolved by `PermissionPolicyProvider` + `PermissionHandler`, cached 10 min; call `IPermissionService.Invalidate()` after editing the matrix).
- Every admin controller inherits **`AdminControllerBase`** (`[Area("Admin"), Authorize]`, loads `Perms`, badge counts, `Success()/Error()` flash, `BackTo()`).
  In views use `Can(Permissions.X)` (from `AdminPage<T>`) to hide buttons — but **always enforce on the server too**.
- Business rules: Secretary has no pricing access; can create/delete invoices but not fully edit; catalog items they add get the price from the price table and it is locked (server recomputes).
  When adding a permission: add the constant, put it in a group, decide the defaults, and remember existing DBs already have rows (add a seed/migration step if old roles need it).
- Password reset (owner decision): forgot-password on the login page emails a link **only for SuperAdmin accounts**. Other users' passwords are set/reset only by a super admin in Users
  (direct, or key button = email link). Non-super admins with `Users.Manage` cannot set passwords or change emails. Tokens: 2 h, single use (`PasswordResetService`).
- Super admin can create Plesk mailboxes / reset mailbox passwords for users (`PleskMailService`, XML API). Generated password is shown once, never stored.
- Cookies: `sm.auth` (HttpOnly, SameSite=Strict, Secure outside Development, 8 h sliding), antiforgery `sm.af` + header `X-CSRF-TOKEN`.
  Lockout after 5 failures. Security stamp revalidated every 5 min.
- Audit: call `AuditService.Log("Persian action", "Entity", id, details)` on important admin writes (then `SaveChanges`).

## 8. Security rules (do not regress)

- `AutoValidateAntiforgeryToken` is global — every POST form needs the token (tag helpers add it automatically; `fetch` POSTs must send `X-CSRF-TOKEN`).
- **CSP is strict: `script-src 'self'`** → **no inline `<script>` code, no `onclick=` attributes**. Put JS in `wwwroot/js/*.js` and wire it via `data-*` attributes.
  JSON data for JS may be emitted as `<script type="application/json">` (see `#sm-texts`). Inline `style=""` is allowed.
  New iframes/hosts must be added to the CSP in `SecurityHeadersMiddleware`.
- Rate limits: `forms` (contact/checkout 5 per 5 min), `login`, `reset`. Honeypot field on public forms.
- Uploads: extension + size + magic-bytes checked, random names, **SVG and executables rejected** (`FileStorageService`). Images are resized client-side (`data-img` inputs).
- Prices and invoice totals are **always computed on the server** (`InvoiceService.Recalculate`, `PricingService`); never trust posted totals.
- Invoices are soft-deleted (`IsDeleted`), excluded from reports, restorable from Trash.
- Secrets never in git: `appsettings.Production.json` is git-ignored; don't put real passwords in `appsettings.json` or docs.
- `/admin` and `/diag` responses get `Cache-Control: no-store` and `X-Robots-Tag: noindex`.

## 9. i18n, RTL and formatting

- Culture: `SiteCultureProvider` — public pages use `?lang=fa|en` (sets cookie `sm.lang`) else cookie, default fa; **/admin is always fa**.
  **Number formatting culture is always `en-US`** (so decimal inputs parse correctly); only the UI culture changes.
- Public UI strings: `Services/Lang.cs` dictionary `Texts` → `L["Key"]` in views (`@inject Lang L` is in `_ViewImports`). Add **both fa and en** for every key.
  Bilingual DB content uses `XxxFa` / `XxxEn` pairs; show with `L.Pick(fa, en)` (falls back to fa).
- JS strings for the public site are passed through `#sm-texts` JSON in `_Layout.cshtml`.
- Layout direction: `_Layout` loads `bootstrap.rtl.min.css` for fa and `bootstrap.min.css` for en and sets `dir`. Use **logical CSS** (`margin-inline-start`, `inset-inline`, `text-align: start`, Bootstrap `ms-*/me-*`) so both directions work.
  Admin is RTL only. Use the `.ltr` class for phone numbers, emails, codes, URLs inside RTL text.
- Dates are stored **UTC**; display Jalali (Persian calendar) with `Fmt.Jalali()` / admin `D()`, Gregorian for en via `L.Date()`. Parse Jalali input with `Fmt.ParseJalali`.
  Date inputs in admin use `data-jdp` (JalaliDatePicker). `InvoiceCheque.DueDate` is a calendar date (no time).
- Numbers: `Fmt.Number` / admin `N()` / `L.Num()` (Persian digits in fa). Money inputs use `data-money` (thousand separators); `FlexibleDecimalBinder` accepts Persian digits and separators.
  Currency is Rial (label from settings). Mobile numbers normalized with `Fmt.NormalizeMobile`.
- Admin modal headers: close **X on the right**, title on the left (owner request). Theme toggle at the far left of the top bar.

## 10. SEO

- `_Layout.cshtml` builds title, description, canonical, hreflang fa/en/x-default, Open Graph, Twitter card from `ViewData`:
  `Title`, `Description`, `Canonical`, `OgImage`, `OgType`, `Robots`. Set them in each public view.
- JSON-LD: Organization/WebSite on home, Product + Breadcrumb on product pages (`Seo.Json`). `Seo.Describe()` trims descriptions.
- `/sitemap.xml` and `/robots.txt` are generated (`SeoController`); new public pages must be added to the sitemap.
- Slugs: `Fmt.Slugify`; product/category `Slug` is unique. Admin pages are noindex.

## 11. UI conventions

- **Design**: logo navy `#22318a` / `#2c3fa0` + gold `#f5b301` (gold buttons use navy text). Keep some colour/animation in admin (owner asked for "colourful and animated"),
  but respect `prefers-reduced-motion`. Light + dark themes via `data-bs-theme` on `<html>` (`theme-init.js` runs early to avoid flash).
- CSS variables: public `--sm-*` in `site.css`, admin `--a-*` in `admin.css`. Reuse them; define dark values under `[data-bs-theme="dark"]`.
- Admin building blocks: `.a-card` / `.a-card-head` / `.a-card-body`, `.a-stat`, `.a-table` (wrap in `.table-responsive`), `.a-actions`, `.pill-*`, `.btn-a`, `.btn-soft*`, `.a-empty`.
  Flash via `Success()/Error()` in controllers. Confirm destructive forms with `data-confirm="..."`.
- Admin JS hooks (`admin.js`): `data-confirm`, `data-money`, `data-jdp`, `data-img`, `data-print`, `data-autosubmit`, `data-side-open/close/collapse`,
  invoice editor (`#invRows`, `data-f`, `data-inv-add/remove`, `#chequeRows`, `data-c`), suggestion popup `#invSuggest` (fixed, on `<body>`).
  Bootstrap modals are moved to `<body>` (the `.a-content` animation creates a stacking context).
- Public JS hooks (`site.js`): `data-add-cart`, `data-variant-select`, `data-qty`, `data-cart-qty`, `data-cart-remove`, `data-count`, `data-autosubmit`. Cart is stored in localStorage and priced by `/api/cart`.
- **Responsive rules** (tested at 360, 390, 768, 1024 px; no page may scroll horizontally):
  - Wrap every table in `.table-responsive`; on phones `.a-table` cells are `nowrap`, so give long text columns a class like `.inv-desc` (wraps, min-width).
  - Inside flex rows, text that should truncate needs `.min-w-0` (custom utility defined in both CSS files — Bootstrap has none) plus `.text-truncate`.
  - Use `.text-break` for user content that can contain long unbroken words/emails (messages, errors).
  - Wide edit tables become cards on phones using `td[data-label]` + CSS grid (see "Responsive fixes" in `admin.css`, used by invoice items and cheques). Reuse that pattern.
  - Forms: Bootstrap grid `col-12 col-md-*`; toolbars `d-flex flex-wrap gap-2`. Mobile public nav is an offcanvas; public site has a bottom bar `.sm-mbar` on phones.
  - Admin sidebar is off-canvas below 992 px (`body.side-open`), collapsible on desktop (`body.side-collapsed`).

## 12. Domain model cheat-sheet

- `Category` (tree via `ParentId`, slug), `Material`, `Product` (code+slug unique, fa/en text, flags IsActive/IsFeatured/CallForPrice)
  → `ProductVariant` (size, class, schedule, unit, SKU, stock) → `PriceRecord` (purchase/sale/cash/cheque price, cheque days, discount, supplier; `IsCurrent` + history; set via `PricingService.SetPriceAsync`).
- `ProductImage` (main / show in home slider), `ProductDocument` (certificate/datasheet, version, issue/expiry, previous versions archived).
- `Customer` (unique mobile), `Order` + `OrderItem` (public cart; number from `InvoiceService.NewOrderNumberAsync`), convertible to invoice.
- `Invoice` (Type pro-forma/sale, Status, payment Cash/Cheque/Mixed/Credit, `CashAmount`+`ChequeAmount`, totals, `CostTotal` for profit, soft delete)
  → `InvoiceItem`, `InvoiceCheque` (sum of cheques must equal `ChequeAmount`). Serial/number from `InvoiceService`.
- `Client` (logo slider), `ContactMessage` (saved even if email fails; resend from admin), `SiteSettings` (single row), `RolePermission`, `AuditLog`, `StoredFile` (upload bytes fallback).

## 13. Git / PR workflow

- Default branch `main`. Work on a feature branch, open a PR, the owner merges. Commit messages in English; UI text and docs for the owner in Persian.
- Before pushing: `dotnet build` must pass with no new warnings you introduced; run the app and check affected pages (fa + en, light + dark, phone + desktop).
- If you change hosting-related behaviour, update `DEPLOY.md` (Persian). If you change features/roles, update `README.md`.
- If you change the DB model, include the migration in the same PR.
