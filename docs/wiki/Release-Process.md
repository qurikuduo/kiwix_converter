# Release Process

## Versioning

- Releases follow semantic version tags such as `v0.1.2`.
- Patch versions are preferred for CI/CD or packaging fixes.
- Major and minor versions should reflect product-facing changes.

## GitHub Actions Flow

1. `ci.yml` runs on pushes to `main` and on pull requests.
2. `release.yml` runs on semantic version tags or by `workflow_dispatch` with an explicit version input.
3. The release workflow builds the WinForms application, publishes the Windows package, generates checksums, and creates a GitHub release.

## Release Assets

- `KiwixConverter-win-x64-vX.Y.Z.zip`
- `KiwixConverter-win-x64-vX.Y.Z.zip.sha256`

## Release Notes

Automatic release notes are configured through `.github/release.yml` and grouped into common sections such as new features, fixes, documentation, and maintenance.