# Releasing

**Status: planned, not built.** CI produces unsigned MSIX packages as build artifacts today (`ci.yml` → `msix-x64`, `msix-ARM64`). Signed releases and auto-update are the R0 items still to do. This page is the design they follow (PLAN.md §6.3).

## Signing

The package `Publisher` in `src/Substratal.Hub/Package.appxmanifest` is `CN=Composite Code`. It must match the signing certificate's subject exactly.

- **Now (1–3 admins):** a self-signed code-signing certificate, trusted once on each admin machine (`Trusted People`). It costs nothing. The `.pfx` lives only in a GitHub Actions secret, never in the repository (`.gitignore` blocks `*.pfx`).
- **Later (more admins, or outside the team):** Azure Trusted Signing (about $10/month), signed from CI with OIDC.

## Distribution and updates

- MSIX plus an `.appinstaller` file in an S3 bucket behind CloudFront at `downloads.substratalapps.com`, in the API's AWS account and Terraform. The cost is about $0–1/month.
- The `.appinstaller` checks for updates on launch, so installing once from App Installer keeps every admin current.
- The GLB-03 **Update** button opens the Windows guide in the help center, which links the installer.

## Release steps (once built)

1. Bump `VersionPrefix` in `Directory.Build.props` and the `Identity Version` in `Package.appxmanifest`.
2. Merge to `main`. A `release.yml` workflow builds, signs, uploads the packages and the `.appinstaller` to S3 through OIDC, and invalidates CloudFront.
3. `docs-dispatch.yml` asks the help center to rebuild, so "What's new" shows the release.
