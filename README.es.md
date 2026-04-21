# Kiwix Converter

Kiwix Converter es una aplicación de escritorio WinForms + SQLite para convertir archivos ZIM descargados con kiwix-desktop a Markdown por artículo y a artefactos JSON listos para RAG.

## Ediciones de idioma

- English: [README.md](README.md)
- 简体中文: [README.zh-CN.md](README.zh-CN.md)
- 日本語: [README.ja.md](README.ja.md)
- Español: [README.es.md](README.es.md)
- العربية: [README.ar.md](README.ar.md)

## Capacidades principales

- Escanea el directorio configurado de kiwix-desktop y sincroniza los archivos ZIM disponibles.
- Usa `zimdump` para leer metadatos, listar artículos, extraer HTML e imágenes.
- Extrae solo el contenido principal del artículo y reescribe los enlaces internos hacia rutas locales de Markdown.
- Genera `content.md`, `metadata.json` y `chunks.jsonl` para importación en sistemas RAG.
- Guarda tareas, registros y checkpoints por artículo en SQLite para pausar, reanudar y recuperar sesiones.

## Requisitos

- Windows
- .NET 8 SDK
- `zimdump` disponible en `PATH` o configurado desde la interfaz

## Flujo de uso

1. Inicia la aplicación.
2. En el primer arranque configura:
   - el directorio `kiwix-desktop`
   - el directorio de salida por defecto
   - opcionalmente, la ruta del ejecutable `zimdump`
3. Haz clic en `Scan ZIM Files` para sincronizar los archivos locales.
4. Selecciona un ZIM en la lista y, si lo necesitas, define una carpeta de salida específica para esa tarea.
5. Inicia la conversión y sigue el progreso, el historial y los registros desde la interfaz.

## Automatización de CI y releases

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) compila el proyecto y sube un artefacto en cada push a `main` y en cada pull request.
- [`.github/workflows/release.yml`](.github/workflows/release.yml) empaqueta la aplicación y publica una release automática con versiones semánticas.
- [`.github/release.yml`](.github/release.yml) define la estructura de las release notes generadas automáticamente.

## Fuentes de la wiki

- Las páginas multilingües de la wiki se guardan en [docs/wiki](docs/wiki).
- Actualmente incluyen la página principal y el proceso de release en inglés, chino, japonés, español y árabe.