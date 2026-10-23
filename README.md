# Substratal Hub for Windows

The Windows edition of the **Substratal Hub**, the client for the [Substratal Apps Platform API](https://compositecode.github.io/substratalapps.com/). It's built management-first: the Administration and Developer sections (users, access, roles, audit, Applications, API keys, webhooks, tenants) come before the personal ones.

| | |
|---|---|
| Stack | WinUI 3 on the Windows App SDK 2.x, C# / .NET 10, MVVM (CommunityToolkit.Mvvm), MSIX |
| API | `https://api.substratalapps.com` with `Substratal-Version: 1` (see [API versioning](#api-versioning)) |
| Specs | [Hub application specs](https://compositecode.github.io/substratalapps-applications-specs/platforms/windows.html): `windows/PLATFORM-SPEC.md`, `windows/SCREENS.md`, `shared/*` |
| Help center | [Substratal Hub help](https://compositecode.github.io/substratalapps-applications-specs/help/), the documentation every Hub app links to |
| Plans | [docs/PLAN.md](docs/PLAN.md) (the app) · [docs/API-GAPS.md](docs/API-GAPS.md) (what the API still needs) |

## Status

A skeleton, at release R0 in [PLAN.md](docs/PLAN.md#5-feature-set-by-release):

- **Built:** sign-in with two-factor (AUTH-01/02); session restore, refresh and sign-out; Capabilities-gated navigation for all three sections and the footer; the command palette (GLB-01); GLB-03 version banners; the Users directory (USR-01) against the live API; Settings › Appearance with all ten themes (SET-01); F1 help for every screen.
- **Routed, not built:** every other screen in `windows/SCREENS.md` opens a page showing its spec entry and a link to its help page, so navigation works end to end.
- **Not in this app:** the web console's Operations, Reports and Integrations screens. The specs have native apps skip them (PRODUCT-SPEC §2a, OPEN-QUESTIONS #27).

## Layout

```
src/Substratal.Hub.Core/     net10.0, cross-platform: API client (Kiota, generated), sessions, Capabilities,
                             screen registry and error copy (generated from the specs), help links, view models
src/Substratal.Hub/          net10.0-windows: WinUI views, Credential Locker, preferences, themes, MSIX manifest
tests/Substratal.Hub.Core.Tests/
api/openapi.yaml             the API contract snapshot the client is generated from
scripts/                     gen-api.sh · gen-from-spec.py · gen-msix-assets.mjs
```

Everything that isn't a view lives in `Core`, so it builds and tests on Linux and macOS as well as Windows.

## Building

Requires the .NET 10 SDK. The WinUI app builds only on Windows (Visual Studio 2022 17.14+ or 2026 with the *WinUI application development* workload, or the .NET SDK alone).

```powershell
dotnet build src/Substratal.Hub -c Debug -p:Platform=x64     # Windows
dotnet test --project tests/Substratal.Hub.Core.Tests          # any OS
```

To run against a local API, start the API repo's devserver (`make up && make run`) and choose **Local devserver** on the sign-in page.

## API versioning

The API version is a **request header, not part of the URL**. Every request goes to the host root and names the version it was written against:

```http
GET https://api.substratalapps.com/users/me
Substratal-Version: 1
Authorization: Bearer <access token>
```

- `ApiVersionHandler` adds the header to every request, including sign-in, refresh and sign-out. Without it, the API answers `400 api_version_required` before authentication runs.
- Older URLs with `/v1` now return `404 not_found`, with no redirect. A pasted `…/v1` environment URL is trimmed to the root (`ApiEnvironment.Root`).
- A response carrying `Deprecation` and `Sunset` shows the GLB-03 "Update available" banner. `400 unsupported_api_version` shows "Update required".
- The generated client tracks the API's additive changes automatically ([api-sync.yml](.github/workflows/api-sync.yml)). Moving to a new `Substratal-Version` is a deliberate change. See [api/README.md](api/README.md).

Verified against production on 2026-10-23: version `1` is accepted, `2` returns `unsupported_api_version`, a missing header returns `api_version_required`, and `/v1/…` returns `404`.

## Keeping in step with the specs, the API and the docs

| What | How |
|---|---|
| Screens and error copy | `scripts/gen-from-spec.py` generates `ScreenCatalog.g.cs` from `windows/SCREENS.md`, and `ErrorCopy.g.cs` from `COPY-AND-STATES.md`. CI fails if either is stale. |
| API client | `scripts/gen-api.sh` regenerates it from `api/openapi.yaml`. CI fails if it's stale. `api-sync.yml` opens a pull request daily when the published contract changes. |
| Help center | `docs-dispatch.yml` asks the help center to rebuild whenever this repo's `main` changes. The help center also rebuilds daily and on API contract changes. |
| Brand assets | `scripts/gen-msix-assets.mjs` renders the MSIX tiles from the brand SVG masters. |

## License

[MIT](LICENSE)
