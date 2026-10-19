# Release process

## Versioning

Users on `1.0.*` pick up patches automatically, so patches must be backwards compatible.

| Release type              | Release contents                                                  |
|---------------------------|-------------------------------------------------------------------|
| Patch (`1.0.0` → `1.0.1`) | Fixes to existing rules, e.g. a false positive                    |
| Minor (`1.0.1` → `1.1.0`) | New rules or stricter existing ones                               |
| Major (`1.1.0` → `2.0.0`) | Removed or renamed rule IDs                                       |

## How to publish

### 1. Update the release tracking

Move the new rules from `AnalyzerReleases.Unshipped.md` into a `## Release x.y.z` section of `AnalyzerReleases.Shipped.md`

### 2. Bump the version

Tags are created manually to bump the version via [dotnet-gitversion](https://www.nuget.org/packages/gitversion.tool/).

To tag the latest commit on main:
```sh
git tag v1.1.0
git push origin v1.1.0
```

> *Note - include the "v" at the start of the version!*

### 3. Publish the new version

TODO
