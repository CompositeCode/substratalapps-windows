# API contract

`openapi.yaml` is a snapshot of the Platform API's published contract,
<https://compositecode.github.io/substratalapps.com/openapi.yaml>. The C# client in
`src/Substratal.Hub.Core/Api/Generated` is generated from it by `scripts/gen-api.sh`.

| | Current |
|---|---|
| `info.version` (contract revision) | `1.0.0-draft.9`, 124 operations |
| `Substratal-Version` (API version) | `1` (`ApiVersion.Current`) |
| Server | `https://api.substratalapps.com`, the host root with no version in the path |

## Two version numbers

- **`Substratal-Version`** is the API version, a whole number sent as a request header on every call. It changes only for a breaking change. The old version keeps working for at least 6 months, and responses to it carry `Deprecation` and `Sunset` headers. See [Conventions → Versioning](https://compositecode.github.io/substratalapps.com/api-reference/conventions/#versioning).
- **`info.version`** in `openapi.yaml` is the contract revision. It moves with every additive change: new endpoints, new optional fields, new enum values. None of those need a new `Substratal-Version`.

## Updating

**Additive changes (most of them).** `api-sync.yml` runs daily, regenerates the client when the published contract changes, and opens a pull request. CI builds the app against it. Merge when it's green. To do it by hand:

```bash
scripts/gen-api.sh --fetch
```

**A new `Substratal-Version`.** This needs a person:

1. Read the API Changelog entry for what broke.
2. Regenerate from that version's `openapi.yaml`, and fix whatever no longer compiles.
3. Bump `ApiVersion.Current` in `src/Substratal.Hub.Core/Api/ApiVersioning.cs`.
4. Ship before the old version's `Sunset` date. Builds still on the old version show "Update available" until then, and "Update required" after it.

Unknown enum values must never be an error (NFR → Versioning). Handle them generically.
