# What the API is missing for the Windows app

What the Substratal Apps Platform API still needs before this app can cover everything in [PLAN.md](PLAN.md). Updated 2026-10-23 against `openapi.yaml` 1.0.0-draft.9 (124 operations, `Substratal-Version: 1`).

**The tracker is the API repo.** Gaps are filed there as [`[api-gaps]` issues](https://github.com/CompositeCode/substratalapps.com/issues?q=is%3Aissue+%22%5Bapi-gaps%5D%22) (G1–G21, #38–#65), shared with the web console and the other Hub apps. This page maps this app's needs onto those issues, and lists the few that only a native client has.

Paths below are written the way the API now serves them: at the host root, with the version in the `Substratal-Version` header, not in the URL.

## Already resolved

| Need | Resolved by |
|---|---|
| Filter users by role (`GET /users?role_id=`, ROL-02 "People with this role") | G17 [#54](https://github.com/CompositeCode/substratalapps.com/issues/54), draft.9 |
| Sign a user out everywhere without suspending them | G14 [#51](https://github.com/CompositeCode/substratalapps.com/issues/51): `POST /users/{id}/sessions/revoke-all` |
| Audit export beyond the list view | G19 [#56](https://github.com/CompositeCode/substratalapps.com/issues/56) / #33: `POST /audit-exports` |
| Self-service Application registration, and the review queue it feeds | #32, draft.7 |
| `last_login_at` on `User` (dormant-account reviews) | Already in the `User` schema |
| Spec drift (Notifications "501", `/v1` paths, CORS headers) | G3 [#40](https://github.com/CompositeCode/substratalapps.com/issues/40), G1 [#38](https://github.com/CompositeCode/substratalapps.com/issues/38) |

## Open, and needed by this app

| Need | Issue | Blocks here |
|---|---|---|
| Server-enforced MFA for staff, and step-up for destructive operations | G13 [#50](https://github.com/CompositeCode/substratalapps.com/issues/50) | Today the app enforces two-factor for staff client-side only (`ShellViewModel.RequiresTwoFactorSetup`) |
| Retire an Application (revoke keys, disable webhooks, remove push credentials, optionally end access) | G15 [#52](https://github.com/CompositeCode/substratalapps.com/issues/52) | APL-03 Lifecycle. Until then, retiring is a guided series of existing calls |
| Runtime settings and client feature flags | G11 [#48](https://github.com/CompositeCode/substratalapps.com/issues/48) | `FeatureFlags` defaults are compiled in; remote config replaces them |
| Higher read rate limit for staff sessions | G4 [#41](https://github.com/CompositeCode/substratalapps.com/issues/41) | Fast paging and bulk fan-out stay inside 100 requests/minute |
| Per-key rate-limit overrides | G12 [#49](https://github.com/CompositeCode/substratalapps.com/issues/49) | KEY-04 |
| Usage per Application, key and operation | G6 [#43](https://github.com/CompositeCode/substratalapps.com/issues/43), G7 [#44](https://github.com/CompositeCode/substratalapps.com/issues/44) | Usage columns on APL-01 and KEY-01 |
| Webhook delivery health across subscriptions | G8 [#45](https://github.com/CompositeCode/substratalapps.com/issues/45) | A "needs attention" list without N calls |
| Exact counts on list responses | G16 [#53](https://github.com/CompositeCode/substratalapps.com/issues/53) | Counts in the navigation and GLB-02 |
| Bulk operations | G18 [#55](https://github.com/CompositeCode/substratalapps.com/issues/55) | Bulk suspend and bulk toggle run one request per row (4 in parallel) until then |
| Passkeys as a second factor | G21 [#65](https://github.com/CompositeCode/substratalapps.com/issues/65) | AUTH-01 `passkeys` flag; Windows Hello passkeys (PLATFORM-SPEC §4) |

The operations issues (G9 job runs [#46](https://github.com/CompositeCode/substratalapps.com/issues/46), G10 `operations.*` permissions [#47](https://github.com/CompositeCode/substratalapps.com/issues/47), G20 subscriber list [#57](https://github.com/CompositeCode/substratalapps.com/issues/57)) serve the web console's Operations screens, which native apps skip (PRODUCT-SPEC §2a, OPEN-QUESTIONS #27).

## Native-only gaps with no issue yet

These matter to the Windows app and aren't covered by an `[api-gaps]` issue. They're candidates to file.

| Gap | Proposal | Why it matters here |
|---|---|---|
| **No Windows push provider** (OPEN-QUESTIONS #2) | Add `wns` as a push provider: per-Application WNS credentials, devices registered with a channel URI | Windows polls `GET /users/me/notifications/unread-count` every 60 s instead (PLATFORM-SPEC §4a). It works, but it's 60 requests an hour per running app, and nothing arrives while the app is closed |
| **Alarms never reach staff in the app** | Bridge the API's CloudWatch alarm SNS topic into platform notifications (category `ops.alarm`) to users holding `operations.view` | Native apps have no Ops gateway, so the inbox is the only way an operator on Windows hears about an outage. Today alarms go to email only |
| **No `request_id` filter on audit events** | Add `request_id` to `GET /audit-events` (the column exists on every event) | Lets support go from an error's request ID (shown under "Details" everywhere) to the state change it made |

## Not gaps

- **CORS** doesn't apply: a native client isn't a browser. The API's `api_key_in_browser` rule also doesn't apply, and the app never sends API keys anyway (PLAN.md §3).
- **The Ops gateway** is the web console's same-origin backend for CloudWatch, Budgets and Lambda. Native apps deliberately don't use it (OPEN-QUESTIONS #27).
