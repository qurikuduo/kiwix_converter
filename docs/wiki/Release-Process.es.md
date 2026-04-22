# Proceso de release

## Versionado

- Las releases siguen etiquetas semánticas como `v0.1.2`.
- Para correcciones de CI/CD o empaquetado se recomienda incrementar la versión patch.
- Las versiones major y minor deben reflejar cambios visibles para el producto.

## Flujo de GitHub Actions

1. `ci.yml` se ejecuta en cada push a `main` y en cada pull request.
2. `release.yml` se ejecuta en cada push a `main`, calcula automáticamente la siguiente versión patch semántica a partir de la última etiqueta de release y también puede ejecutarse mediante `workflow_dispatch` con una versión explícita.
3. El flujo de release compila la aplicación WinForms, publica el paquete de Windows, genera checksums y crea una GitHub Release.

## Artefactos de release

- `KiwixConverter-win-x64-vX.Y.Z.zip`
- `KiwixConverter-win-x64-vX.Y.Z.zip.sha256`

## Release notes

Las release notes automáticas se configuran mediante `.github/release.yml` y se organizan con secciones habituales como nuevas funciones, correcciones, documentación y mantenimiento.