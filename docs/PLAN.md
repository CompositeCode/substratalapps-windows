# Substratal Admin for Windows — Application Plan

The Windows management application for the Substratal Apps Platform API (`https://api.substratalapps.com`, with `Substratal-Version: 1` on every request). It is the first client built against the API, and its job is to let platform staff run the API: users, access, roles, applications, credentials, webhooks, push, audit, and the operational health of the system itself.

Companion document: [API-GAPS.md](API-GAPS.md), what the API needs to add for this app to cover monitoring, usage analytics, configuration, and alerting.

## Updates since this plan was written (2026-10-23)

Three things changed between 2026-10-21 and 2026-10-23. Where this page and the specs disagree, **the specs win**.

1. **API versioning moved to a header.** The API version is the `Substratal-Version` request header (`1` today, required on every operation except `GET /health` and `POST /auth/oauth/token`). There's **no version in the URL any more**: the base URL is `https://api.substratalapps.com`, and `/v1/…` paths return `404` with no redirect. A breaking change gets the next version number; the old one keeps working for at least 6 months, with `Deprecation` and `Sunset` response headers. `openapi.yaml` is at `1.0.0-draft.9` (124 operations) and declares the header as the shared `ApiVersion` parameter. The app sends it from one place (`ApiVersionHandler`), shows GLB-03 "Update available" and "Update required" from those headers, and the behavior was verified against production on 2026-10-23. See [README → API versioning](../README.md#api-versioning) and [api/README.md](../api/README.md).
2. **Native apps skip the console screens.** The specs (PRODUCT-SPEC §2a, OPEN-QUESTIONS #27, 2026-10-22) put Operations, Security events, Configuration, Reports and Integrations in the **web** console only, because they read AWS through the web Hub's same-origin Ops gateway. The Operations section and OPS screens in §4.1–§4.8 below are therefore **not built in the Windows app**; they stay here as the record of what was proposed. Revisiting it needs the gateway reachable with a User token (OPEN-QUESTIONS #27).
3. **API gaps are tracked as issues.** The gaps in [API-GAPS.md](API-GAPS.md) are filed in the API repo as [`[api-gaps]` issues](https://github.com/CompositeCode/substratalapps.com/issues?q=is%3Aissue+%22%5Bapi-gaps%5D%22) (G1–G21). Several are already resolved: role filter, revoke-all sessions, audit export, self-service registration.

**Build status:** R0 is under way. The skeleton is in this repo (see [README → Status](../README.md#status)).

Sources this plan is built on (read 2026-10-21):

- API spec: <https://compositecode.github.io/substratalapps.com/> (`openapi.yaml` 1.0.0-draft.3, 118 operations) and the API repo (`CompositeCode/substratalapps.com`, branch `main`, including `plan-remaining.md`, `infra/README.md`, and the Go source).
- UI specs: <https://compositecode.github.io/substratalapps-applications-specs/> (`windows/PLATFORM-SPEC.md`, `windows/SCREENS.md`, `00-shared/*`).
- Production: `GET https://api.substratalapps.com/health` returns `200 {"status":"ok"}`; JWKS is published.

---

## 1. Readiness verdict

**Yes, building can start now.** The API fully covers about 70% of what a management app needs. The remaining 30% (monitoring, usage analytics, runtime configuration, alert routing, and application retirement) needs API work, described in [API-GAPS.md](API-GAPS.md). Those screens should still be built, behind feature flags, using the same "`501`/`404` turns the flag off" rule the UI specs already define. Then each one goes live as soon as its endpoint ships.

| Area | API support today | Notes |
|---|---|---|
| Admin sign-in, MFA, sessions | ✅ Complete | `POST /auth/login`, `/auth/mfa/verify`, rotating refresh, per-user session list/revoke |
| Users (directory, invite, suspend, delete, export, erasure) | ✅ Complete | No filter by role (`?role_id=`) |
| Access / Entitlements | ✅ Complete | Per-user and cross-user lists; `If-Match` + `Idempotency-Key` supported |
| Roles & permissions | ✅ Complete | Custom platform roles, app roles, effective-permissions check |
| Applications (register, edit, review status, push credentials) | 🟡 Partial | **No way to retire or remove an Application**, and suspension doesn't cascade |
| API keys (create, rotate, revoke, scope, expiry, agent restrictions) | ✅ Complete | `last_used_at` exists; **no usage counts, no per-key rate-limit override** |
| Webhooks (subscriptions, deliveries, redeliver, test, rotate) | ✅ Complete | |
| Organizations, Tenants, tier-change requests | ✅ Complete | |
| Audit log | ✅ Complete (hot window, 1 year) | No `request_id` filter, no export (issue #33) |
| Notifications (send, inbox, devices) | ✅ Complete | The specs package still says `501`; that's out of date |
| **Monitoring (traffic, errors, latency, DB load, queues)** | ❌ None | Data exists in CloudWatch; no API exposes it |
| **Usage analytics per app / key / operation** | ❌ None | **The data isn't captured**: the API logs only errors and security events, not every request |
| **Logs / request trace** | ❌ None | CloudWatch Logs only; no API |
| **Background jobs status** | ❌ None | 11 scheduled jobs; no run history |
| **Runtime configuration / feature flags** | ❌ None | Rate limits are hard-coded in Go; the client flags in the specs have no endpoint |
| **Alerts to admins** | 🟡 Email only | CloudWatch alarms → SNS → email. Nothing reaches the app |

### Production prerequisites (not code, but blocking real use)

These are open `[bespoke]` issues in the API repo. For development, the local devserver (`make up && make run`) replaces production entirely, so none of them block building.

- **#20 Set the superadmin password.** No one can sign in to production until this is done.
- **#22 SES out of sandbox.** Needed for invitations and password resets to real addresses.
- **#25 Register Substratal's Applications.** This includes `app_hub`, which the inbox and notification features depend on.
- **#21 Confirm the alert emails.** Needed until in-app alerting exists.

---

## 2. Scope and positioning

The UI specs describe one product, the **Substratal Hub**, in six editions. The Windows edition specifies all 75 screens: personal, administration, and developer. This plan keeps that spec as the contract and changes the **build order and emphasis** for Windows:

1. **Admin first.** The Windows app ships the Administration and Developer sections first, plus a new **Operations** section the specs don't have yet (8 new screens, `OPS-01`…`OPS-08`, defined below).
2. **Personal screens are slimmed down to what an operator needs:** sign-in, two-factor, account security, the inbox (where alerts land), and settings. The consumer screens (app launcher home, Plan & Billing checkout, org-membership flows as a member) stay in the spec and come later.
3. **Same IDs, routes, copy, and status semantics** as the specs, so later editions stay consistent. The `OPS-*` screens should be added back into the specs package (`00-shared/SCREEN-SPECS.md`, `INFORMATION-ARCHITECTURE.md`, `API-SCREEN-MAP.csv`).

Personas served, in priority order: **Superadmin** (platform owner), **Support**, **Billing admin**, then **App owner/developer** (confined to their own Applications). Operations screens are visible only to the new `ops.view`/`ops.manage` permissions proposed in API-GAPS.md. Until those exist, they're visible only to `superadmin`.

---

## 3. Authentication and credential model

### 3.1 How admins sign in

The admin app **never authenticates with an API key.** Every admin signs in as themselves, with a platform user session:

| Step | Implementation |
|---|---|
| Sign in | `POST /auth/login` → `AuthSession`, or `MfaChallenge` → `POST /auth/mfa/verify` |
| Access token (15 min JWT) | Memory only |
| Refresh token (rotating, single use) | Windows **Credential Locker** (`PasswordVault`), one entry per environment. The rotated token is persisted atomically *before* the new access token is used |
| Unlock on launch | **Windows Hello** (`UserConsentVerifier`) gates reading the refresh token. Required for any account holding a platform role with permissions (opt-out not offered) |
| Refresh | Single-flight: on `401`, pause the request queue, refresh once, then replay. Never refresh in parallel, because reusing a rotated token revokes the session and raises `security.session_reuse_detected` |
| Idle lock | 15 minutes idle → Hello re-prompt (the session stays alive). Admin windows are excluded from screen capture while a secret is shown (`WDA_EXCLUDEFROMCAPTURE`) |
| Sign out | `POST /auth/logout`, then wipe the vault entry, caches, and inbox cache |

Why user sessions and not keys: every admin action then lands in the audit log as **that person** (`actor.type = user`), with MFA and lockout protection, a 15-minute token lifetime, and server-side revocation. A platform API key on an admin's laptop would be a long-lived bearer credential with none of those properties.

**MFA policy:** the app requires TOTP for any account with a non-`member` platform role. If `mfa_enabled` is false, it routes to ACC-05 (turn on 2FA) before the shell opens. This is client-side enforcement for now; server-side enforcement is gap **G-07**.

### 3.2 How the app manages API keys for others

The app is the tool for issuing keys to services. It doesn't hold them itself.

- **Create** (KEY-02): scope (platform or one Application), permissions checklist limited to what the scope allows, mode (`live`/`test`), `intended_use` (`service`/`agent`, which defaults `restrict_destructive`), and **expiry, defaulting to 1 year** (the API allows none; the UI nudges against it).
- **Reveal once** (KEY-03): the secret is shown once in a ContentDialog that's excluded from screen capture. Copying puts it on the clipboard with `IsAllowedInHistory=false` and `IsRoamable=false`, auto-cleared after 60 seconds. It's never written to disk or logs.
- **Rotate** (`POST /api-keys/{id}/rotate`) is immediate, with no overlap. The UI says so plainly and offers "Create a second key first" as the zero-downtime path.
- **Hygiene views** built from existing data: keys expiring in the next 30 days, keys never used, keys unused for 90+ days (`last_used_at`), platform-scoped keys, and agent keys without `restrict_destructive`. One-click revoke from each.
- **Usage per key** (requests, errors, rate-limit hits) needs gap **G-02**.

### 3.3 Environments

An environment switcher on the sign-in page, also reachable from Settings: **Production** (`https://api.substratalapps.com`), **Local devserver** (`http://localhost:8080`), and **Custom** (for a future staging account). Each environment keeps its own credentials and caches. Non-production environments show a persistent colored band in the title bar. A `test_mode` session (from a test-mode signup) shows a TEST badge, per OPEN-QUESTIONS #13.

---

## 4. Interface design

Fluent 2 / Windows 11, as specified in `windows/PLATFORM-SPEC.md` (Mica backdrop, NavigationView Left, custom 48 epx title bar with a centered command palette, Moss Paper / Forest Floor themes). The navigation tree changes as follows:

```
Substratal Admin
├── OPERATIONS                         (new: ops.view; superadmin until G-01 ships)
│   ├── Overview          /ops                    OPS-01   Ctrl+Shift+O
│   ├── Usage             /ops/usage              OPS-02
│   ├── Health & alarms   /ops/health             OPS-03
│   ├── Request trace     /ops/trace[/:requestId] OPS-04
│   ├── Security events   /ops/security           OPS-05
│   ├── Jobs              /ops/jobs               OPS-06
│   ├── Configuration     /ops/config             OPS-07
│   └── Integrations      /ops/integrations       OPS-08
├── ADMINISTRATION
│   ├── Users             /admin/users            USR-01 (+ USR-03 tabs: Access, Roles, Sessions, Notifications, Activity)
│   ├── Access            /admin/access           ACX-05
│   ├── Roles             /admin/roles            ROL-01
│   ├── Organizations     /admin/orgs             ORG-01 (all orgs, organizations.manage)
│   ├── Audit log         /admin/audit            AUD-01
│   └── Send notification /admin/notifications/new NTF-06
├── APPLICATIONS                       (the specs' "Developer" section, renamed for an admin audience)
│   ├── Applications      /dev/apps               APL-01 (+ APL-03 tabs: Overview, Usage, Access, Roles, Settings schema, Keys, Webhooks, Push, Review, Lifecycle)
│   ├── API keys          /dev/keys               KEY-01
│   ├── Webhooks          /dev/webhooks           WHK-01
│   └── Tenants           /dev/tenants            BIL-04 (all tenants, tenants.manage)
└── Footer: Inbox (badge) · Account & security · Settings
```

The Inbox moves to the footer, because for an operator it's an alert feed, not a personal home.

### 4.1 OPS-01 Overview (the landing page for ops roles). *Web console only; see Updates above*

```
┌ Substratal ─────────────────[ Search or jump to…  Ctrl+K ]──────────── PROD ─ ▢ ✕ ┐
│ ≡ OPERATIONS     │ Overview                               Last 24 h ▾   ⟳  Ctrl+R │
│   Overview  ◀    │ ┌──────────┐┌──────────┐┌──────────┐┌──────────┐┌──────────┐ │
│   Usage          │ │Requests  ││Error rate││p95 hot   ││Availabil.││Open      │ │
│   Health 2       │ │ 48.2k    ││ 0.21 %   ││ 92 ms    ││ 99.97 %  ││alarms  2 │ │
│   Trace          │ │ ▁▂▃▅▃▂▁  ││ ▁▁▁▂▁▁▁  ││ SLO 150  ││ SLO 99.9 ││ ● ●      │ │
│   Security       │ └──────────┘└──────────┘└──────────┘└──────────┘└──────────┘ │
│   Jobs           │ Traffic by application (req/min)          Needs attention    │
│   Config         │ ┌────────────────────────────────────┐ ┌─────────────────┐ │
│   Integrations   │ │  stacked area: timetrack, invoicer │ │⚠ webhook wh_…   │ │
│ ADMINISTRATION   │ │  hub, other                        │ │  unhealthy (3d) │ │
│   Users          │ │                                    │ │⚠ APNs cred app_ │ │
│   Access …       │ └────────────────────────────────────┘ │  invoicer failed│ │
│ APPLICATIONS     │ Top errors (code · op · count)          │⚠ 3 keys expire  │ │
│   …              │ ┌────────────────────────────────────┐ │  in < 30 days   │ │
│ ──────────────── │ │ 429 rate_limited · effective-p… 312│ │⚠ erasure due in │ │
│   Inbox 4        │ │ 403 forbidden · users.list      41 │ │  2 days (1)     │ │
│   Account        │ └────────────────────────────────────┘ │⚠ tier request   │ │
│   Settings       │ Recent admin activity (audit)          │  awaiting (1)   │ │
│                  │  14:02 dana suspended usr_… · 13:50 …  └─────────────────┘ │
└──────────────────┴─────────────────────────────────────────────────────────────┘
```

- **KPI tiles** (G-01): requests, 5xx error rate, p95 latency of the hot paths against the 150 ms SLO, monthly availability against the 99.9% SLO, and open alarms.
- **Traffic by application** (G-02): a stacked area chart.
- **Needs attention:** composed **client-side from endpoints that exist today**, so it ships in Phase 1. That's unhealthy or disabled webhooks, push credentials in a failed state, keys expiring or unused, scheduled erasures, open tier-change requests, Applications `pending_review`, and suspended Organizations. Each row deep-links to the fix.
- **Recent admin activity:** `GET /audit-events?actor_type=user&limit=10`.
- Refresh: every 60 seconds while visible, paused when minimized. `Ctrl+R` refreshes now.

### 4.2 OPS-02 Usage and analytics

Filters: time range (1 h / 24 h / 7 d / 30 d / custom), mode (live/test), Application, API key, operation, and status class. A group-by selector (Application · API key · operation · status · tenant) drives one time-series chart and a sortable table: requests, error %, 429s, p50/p95/p99, and last seen. Rows drill into the Application's or key's Usage tab. The **Export CSV** button writes exactly what's on screen through `FileSavePicker`. Backed by `GET /ops/usage` (G-02).

### 4.3 OPS-03 Health and alarms

- A **dependency strip** (G-01 deep health): API Lambda, Aurora (ACU vs. ceiling), KMS, SQS queue depth, webhook DLQ depth, SES send quota and bounce rate, Stripe reachability, the deployed commit SHA, and the migration version.
- An **alarm list** (CloudWatch alarm state, reason, and last transition) with history.
- **SLO panel:** availability and latency over the month, with error budget remaining.
- **Cost panel** (optional, G-09): month-to-date spend against the $75 warn and $150 hard budgets.

### 4.4 OPS-04 Request trace

A search box takes an `X-Request-Id`. Every error message in the app already shows its request id under "Details", so support can paste one in. The result is a timeline of log lines for that request (op, status, latency, actor, IP, error code), the Audit Events written by that request (needs the `request_id` filter, G-05), and the webhook events it enqueued. A second mode, **by actor**, shows the last N requests from a user or key. It only ever runs server-side **templated** queries, never free-text Logs Insights (G-03).

### 4.5 OPS-05 Security events

Failed logins, lockouts, refresh-token reuse, `401`/`403`/`429` bursts, and `destructive_operation_restricted`, with counts over time and a table by IP, account, and key. Row actions: open the user (USR-03), revoke a session, suspend the user, or revoke the key. Each action is the existing API call, with a confirmation.

### 4.6 OPS-06 Jobs

The 11 scheduled jobs (`entitlement-sweep`, `webhook-dispatch`, `webhook-health`, `push-maintenance`, `erasure-cascade`, `subscription-access`, `stripe-events`, `seat-sync`, `cleanup`, `audit-archive`, `settings-projections`), each with its schedule, last run, duration, result, and items processed. **Run now** needs `ops.manage` and a confirmation (G-04).

### 4.7 OPS-07 Configuration

Grouped SettingsCards. Each change goes through a **diff-and-confirm** dialog and writes an audit event (G-06).

- **Access:** signup open/closed, invitation-only mode, require MFA for staff (G-07), session idle timeout.
- **Rate limits:** default per-principal limit and the hot-path limit. Per-key overrides live on the key itself (G-08).
- **Client feature flags:** the flags listed in CLIENT-ARCHITECTURE §10 (`organizations`, `webhooks`, `billing`, …), served to every Hub client.
- **Maintenance:** read-only mode, with a banner message.
- **Read-only facts** (from deep health): region, tenancy tiers in use, CORS origins, and the webhook `api_version`.

Infrastructure settings (Lambda memory, Aurora ACU limits, budgets) stay in Terraform. The app links to them but never edits them, because Terraform is the system of record for infrastructure.

### 4.8 OPS-08 Integrations

- **Platform-scoped webhooks** (already supported) for feeding events into external tools.
- **Alert routing:** which alarm and security categories create in-app alerts, and for which roles (G-10).
- **Outbound channels:** a Slack or Teams incoming-webhook URL for alarms (G-10).
- **Copyable configs:** the MCP server URL for agent integrations, and the OpenAPI document link.

### 4.9 Application management (APL-01 / APL-03, extended)

APL-01 lists every Application, with columns for name and slug, owner, Tenant, `access_tier`, `review_status`, visibility, active-access count, requests (24 h, from G-02), error %, webhook health, and push credential state. Filters cover the review queue (`pending_review`, FIFO, matching the spec's review lifecycle), suspended, and retired.

APL-03 tabs:

| Tab | Content | API |
|---|---|---|
| Overview | Catalog fields, owner, tenant, launch URL, default app role | `GET/PATCH /applications/{id}` |
| **Usage** | Requests, errors, latency, top operations, top keys | G-02 |
| Access | Everyone with an access path, with status and source; bulk turn-off | `GET /entitlements?application_id=` |
| Roles | App roles and permission vocabulary | `/roles?scope=`, `available_app_roles` |
| Settings schema | Edit and validate `settings_schema` | `PATCH /applications/{id}` |
| Keys | App-scoped keys, plus "create key for this app" | `/api-keys?scope=` |
| Webhooks | App-scoped subscriptions and delivery health | `/webhooks` |
| Push | APNs / FCM / Web Push credentials | `/applications/{id}/push-credentials` |
| Review | Approve, reject, or suspend (notes required) | `PATCH review_status` |
| **Lifecycle** | Suspend; **Retire** (guided: revoke keys → disable webhooks → remove push credentials → choose whether to end existing access → retire) | Composable today; one-call retire needs G-11 |

**Add Application** is APL-02 (`POST /applications`, `applications.manage`). **Remove** is the Lifecycle tab's Retire flow. There's no hard delete, by design: Entitlement and audit history must survive.

### 4.10 Reporting

- Every list and chart has **Export CSV** (what's on screen, paged in full with a progress bar and cancel).
- Under Reports in the command palette, there are three **built-in reports** generated client-side: *Access review* (every platform role holder and their permissions, for SOC 2 quarterly reviews), *Key inventory* (every key with scope, permissions, age, expiry, and last used), and *Application summary* (apps with owners, user counts, and usage). Output is CSV, plus a printable HTML report through `PrintManager` or save-as-PDF.
- Audit export of more than the hot window waits for issue #33 (G-12).

---

## 5. Feature set by release

| Release | Contents | Depends on |
|---|---|---|
| **R0 Foundation** (2 weeks) | Solution skeleton, generated API client, sign-in + MFA + Hello + single-flight refresh, environment switcher, Capabilities gating, shell and navigation, themes, command palette (ID-prefix routing), error/offline/429/501 handling, CI build + MSIX + signed publish | Nothing |
| **R1 Administration** (3–4 weeks) | Users (directory, invite, suspend, sessions, export, erasure), Access (grant, toggle with undo, why?), Roles (platform and app, assignments), Organizations admin, Audit log, Send notification, Inbox with polling and local toasts and badge | Nothing (API complete) |
| **R2 Applications & credentials** (3 weeks) | Applications list/register/edit/review/push credentials, API keys with hygiene views, Webhooks with deliveries and redeliver, Tenants and tier-change requests, **OPS-01 v0** (Needs-attention + audit feed + `/health`), CSV export, built-in reports | Nothing |
| **R3 Operations** (3 weeks) | OPS-01 full, OPS-02 Usage, OPS-03 Health & alarms, OPS-04 Trace, OPS-05 Security, OPS-06 Jobs, Application Usage tab, key usage | API gaps G-01 to G-05 |
| **R4 Control** (2 weeks) | OPS-07 Configuration, OPS-08 Integrations, in-app alerts, Retire flow as one call, MFA enforcement, per-key rate limits | API gaps G-06 to G-11 |

The estimates assume one developer working with an AI assistant, the same team shape the API was built with. Each release is usable on its own.

---

## 6. Technology stack

### 6.1 Client

| Concern | Choice | Why |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, C# | Current LTS; supported through Nov 2028 |
| UI | **WinUI 3 / Windows App SDK** (latest stable), packaged | Matches the Windows platform spec (Fluent 2, Mica, NavigationView). Native, fast startup, and first-party |
| Startup / size | Trimmed, ReadyToRun, with **Native AOT as a goal** once dependencies allow; `System.Text.Json` source generation | Sub-second cold start; no reflection-based JSON |
| Pattern | MVVM with **CommunityToolkit.Mvvm**; DI and configuration through **Microsoft.Extensions.Hosting** | Specified; testable view models |
| API client | **Kiota**, generated from the API's `openapi.yaml` (pinned to a release tag), committed, and regenerated in CI | Kiota's backing store sends only the properties that changed, which matches the API's **PATCH field-presence and explicit-`null`** semantics. NSwag doesn't |
| HTTP | One `HttpClient` with `SocketsHttpHandler` (HTTP/2, connection pooling, `PooledConnectionLifetime` 5 min), Brotli/gzip, and **Microsoft.Extensions.Http.Resilience** handlers implementing the specs' retry rules (reads retry twice with jitter; writes retry only with an `Idempotency-Key`; honor `Retry-After`) | Efficient: one warm HTTP/2 connection to API Gateway multiplexes the dashboard's parallel calls |
| Tables | **WinUI.TableView** (sortable, filterable, virtualized) | Spec names a DataTable/TableView |
| Charts | **LiveCharts2** (SkiaSharp, WinUI) | Native rendering, theming hooks; follows the shared dataviz palette in the themes |
| Secure storage | Credential Locker plus Windows Hello consent | Specified |
| Cache | Memory only for admin data (spec §7); SQLite (`Microsoft.Data.Sqlite`) for preferences, the inbox first page, and the Applications catalog, with the key protected by DPAPI | Spec rule: admin lists aren't persisted |
| Local logs | `Microsoft.Extensions.Logging` → rolling file in `LocalState\logs`, with a redaction filter (no tokens, secrets, emails, or notification bodies) | Spec §11 |
| Notifications | `AppNotificationManager` local toasts from inbox polling (60 s), taskbar badge, optional tray + `StartupTask` | No WNS in the API (G-13) |
| Tests | xUnit for `Core` (view models and services, using a fake API); **integration tests of `Core` against the real devserver** on an Ubuntu runner; **FlaUI** UI smoke tests on Windows; Accessibility Insights FastPass | See the solution layout below |

### 6.2 Solution layout

```
Substratal.Admin.sln
├── src/Substratal.Admin.Core/        net10.0 (cross-platform): generated API client, session/refresh,
│                                     Capabilities, domain services, view models, CSV/report builders
├── src/Substratal.Admin.App/         net10.0-windows10.0.19041: WinUI views, navigation, platform
│                                     services (vault, Hello, toasts, badge, file pickers, clipboard)
├── src/Substratal.Admin.Package/     MSIX packaging, App Installer manifest, brand assets
├── tests/Substratal.Admin.Core.Tests/        unit
├── tests/Substratal.Admin.Integration.Tests/ Core against `cmd/devserver` + Postgres (Linux runner)
└── tests/Substratal.Admin.UI.Tests/          FlaUI smoke, Windows runner
```

Keeping every non-UI line in a cross-platform `Core` project is the important decision. GitHub's Windows runners can't run the API's Linux containers (Postgres, LocalStack, Mailpit). Putting `Core` in its own project lets the integration suite run on Ubuntu against the real API devserver, the same way the API's own CI does.

### 6.3 AWS and distribution, kept cheap

The app runs on admins' machines, so the only AWS cost is distribution plus whatever the new ops endpoints add.

| Piece | Choice | Monthly cost |
|---|---|---|
| Installer hosting | MSIX + `.appinstaller` (auto-update on launch) in a private-by-default S3 bucket behind CloudFront at `downloads.substratalapps.com` (existing Route 53 zone) | ≈ $0–1 |
| CI/CD | GitHub Actions: build and test on `windows-latest`, integration on `ubuntu-latest`, sign, and upload to S3 through **OIDC** (no stored AWS keys, same pattern as the API repo) | $0 (within free minutes) |
| Code signing | **Start:** a self-signed certificate trusted on the 1–3 admin machines ($0). **Later**, with more admins: Azure Trusted Signing (≈ $10) | $0 → ≈ $10 |
| Ops endpoints (G-01–G-04) | One more Go Lambda in the API stack, arm64, outside any VPC; CloudWatch `GetMetricData` / Logs Insights with a 60 s server-side cache | ≈ $1–3 |
| Per-request access log (G-02) | ≈ 300 bytes per request into existing CloudWatch Logs (30-day retention) | ≈ $0.15 per million requests |
| Usage rollups | One nightly job into an Aurora table (on the existing cluster) | ≈ $0 |
| **Total incremental** | | **≈ $1–5/month** |

Considered and not chosen:

- **The desktop app calling CloudWatch directly** with AWS credentials. That would put IAM credentials on admin laptops and split authorization across two systems. Going through the API keeps one auth model and one audit trail.
- **A separate admin backend (BFF).** The API spec's "admin console calls through its own backend" rule exists for browsers and CORS, and doesn't apply to a native client. A BFF would add a component to run and pay for with no benefit.
- **Electron / Tauri.** Not native, and heavier.
- **WPF.** Mature, but not Fluent 2.
- **MAUI / Avalonia.** Their cross-platform value is wasted, because the specs already choose SwiftUI and GTK for the other desktops.
- **CloudWatch dashboards** (≈ $3 each per month). They're unnecessary once the app renders the metrics.

### 6.4 Efficiency against the API

- **Parallel loads:** the dashboard fires its 6–10 reads concurrently over one HTTP/2 connection. The API runs in `us-east-1` behind API Gateway HTTP API with Go Lambdas (cold starts in the tens of milliseconds).
- **Rate-limit budget:** the API allows 100 requests/minute per session. Dashboard polling uses at most about 15 requests per minute, and bulk fan-out (for example, suspending 40 users) runs at most 4 requests in parallel and reads `RateLimit-Remaining` to slow down before it hits a `429`.
- **Caching:** Capabilities and the role/permission catalog are cached for an hour, and `ETag`s are kept for `If-Match`. Visible lists aren't refetched on back-navigation.
- **Server-side aggregation:** the ops endpoints return aggregates, so the client never downloads raw logs or metrics.

---

## 7. Cross-cutting implementation details

- **Capabilities**, computed at sign-in per CLIENT-ARCHITECTURE §5, plus `canViewOps` and `canManageOps`. Every navigation item and action reads from Capabilities. A `403` triggers a background refresh.
- **Writes:** an `Idempotency-Key` is generated when a create form opens, and `If-Match` is sent on every PATCH/DELETE of Entitlements, Roles, Settings, Users, and Organizations. A `409 version_conflict` offers **Reload** or **Re-apply**.
- **Destructive actions** (suspend, revoke, delete, erasure, retire, rotate): a confirmation dialog that names the target. High-impact ones (retire an Application, erase a user, revoke a platform key) require typing the name. Access toggles get the specs' 10-second Undo.
- **Errors:** map `error.code` to the copy in COPY-AND-STATES. The raw message and `X-Request-Id` sit behind a "Details" expander, with a **Trace this request** link to OPS-04.
- **Accessibility, keyboard, and touch:** as in PLATFORM-SPEC §3 and §6. New shortcuts: `Ctrl+Shift+O` (Overview), `Ctrl+Shift+T` (Trace), and `Ctrl+Shift+E` (export the current list).
- **Telemetry:** none by default. If it's ever added, it follows CLIENT-ARCHITECTURE §12 (no PII, opt-in).

---

## 8. Changes to propose back to the specs package

1. Add the `OPS-01`…`OPS-08` screens, the `ops.view` / `ops.manage` visibility rules, and the API-SCREEN-MAP rows for the new endpoints.
2. Mark the Notifications operations as implemented; remove the "501 today" notes (PRODUCT-SPEC §4, SCREEN-SPECS GLB-03, OPEN-QUESTIONS #16).
3. APL-03: add the Usage and Lifecycle (retire) tabs.
4. KEY-02: default expiry of one year; hygiene views on KEY-01.
5. Admin MFA requirement in CLIENT-ARCHITECTURE §3.
