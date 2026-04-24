# Kiwix Converter

[![CI](https://github.com/qurikuduo/kiwix_converter/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/qurikuduo/kiwix_converter/actions/workflows/ci.yml)
[![Release Workflow](https://github.com/qurikuduo/kiwix_converter/actions/workflows/release.yml/badge.svg?branch=main)](https://github.com/qurikuduo/kiwix_converter/actions/workflows/release.yml)
[![Latest Release](https://img.shields.io/github/v/release/qurikuduo/kiwix_converter?display_name=tag&sort=semver)](https://github.com/qurikuduo/kiwix_converter/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-F2C94C.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=.net)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D6?logo=windows11)](https://github.com/qurikuduo/kiwix_converter/releases/latest)
[![UI Languages](https://img.shields.io/badge/UI%20Languages-English%20%7C%20%E7%AE%80%E4%BD%93%E4%B8%AD%E6%96%87%20%7C%20%E6%97%A5%E6%9C%AC%E8%AA%9E%20%7C%20Espa%C3%B1ol%20%7C%20%D8%A7%D9%84%D8%B9%D8%B1%D8%A8%D9%8A%D8%A9-0A7C86)](#ediciones-de-idioma)

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
- .NET 8 Desktop Runtime para ejecutar la aplicación empaquetada
- .NET 8 SDK si quieres compilar el proyecto desde el código fuente
- `zimdump` disponible en `PATH` o configurado desde la interfaz

## Archivos de ejecución de escritorio

- La aplicación empaquetada ahora intenta guardar sus datos de ejecución junto al EXE para que la build sea portátil y fácil de inspeccionar.
- La configuración de SQLite y el estado de tareas se guardan en `data/kiwix-converter.db`.
- Los registros de arranque y de ejecución se escriben en `logs/kiwix-converter-YYYY-MM-DD.log`.
- Si la carpeta publicada no permite escritura, la aplicación vuelve a `%LocalAppData%\KiwixConverter`.

## Captura de pantalla

La siguiente imagen se capturó desde la build publicada actual para Windows.

![Ventana principal de Kiwix Converter](docs/images/app-main-window.png)

## Diseño de arquitectura

- `KiwixConverter.WinForms` controla la shell de escritorio, la entrada de configuración, las tablas de tareas, los estados visibles y el flujo operativo del usuario.
- `KiwixConverter.Core` concentra el escaneo, la conversión, la sincronización con WeKnora y la persistencia en SQLite para mantener delgada la capa de UI.
- `zimdump` es el límite de acceso a los archivos ZIM para metadatos, listados de artículos, HTML y recursos.
- La API HTTP de WeKnora es el límite de sincronización RAG para descubrir bases, cargar modelos, crear KB y subir artículos.
- El trabajo de larga duración se modela como tareas persistidas con checkpoints por artículo, por lo que un reinicio no obliga a reconvertir todo el archivo.

## Flujo técnico

1. El escaneo del directorio hace upsert del inventario local de ZIM en SQLite antes de iniciar cualquier conversión.
2. La conversión usa `zimdump` para obtener metadatos y HTML, extrae el contenido principal, reescribe enlaces, exporta imágenes y genera artefactos Markdown y JSON.
3. Cada artículo guarda `content.md`, `metadata.json`, `chunks.jsonl` y su checkpoint, de modo que un fallo solo repite el tramo local que realmente falló.
4. La sincronización con WeKnora lee exportaciones completadas, carga IDs de modelos desde `/api/v1/models`, resuelve o crea bases con la configuración de chunk y sube el Markdown por artículo como conocimiento manual reanudable.

## Inicio rápido para usuarios no técnicos

Si solo quieres usar la aplicación, lo más fácil es descargar el zip de GitHub Releases e instalar .NET 8 Desktop Runtime. Solo necesitas el SDK completo si vas a abrir la solución en Visual Studio o compilarla tú mismo con `dotnet build`.

### 1. Instalar .NET

- Para ejecutar la aplicación empaquetada: instala .NET 8 Desktop Runtime para Windows x64.
- Para compilar desde el código fuente: instala .NET 8 SDK.
- Después de la instalación, vuelve a abrir la terminal o la aplicación para que `dotnet` quede disponible en `PATH`.

### 2. Instalar `zimdump`

Kiwix Converter no lee los archivos ZIM directamente. Usa `zimdump`, que forma parte de las herramientas de Kiwix.

Configuración típica en Windows:

1. Descarga un paquete de herramientas de Kiwix que incluya `zimdump.exe`.
2. Extráelo en una carpeta fija como `C:\Kiwix\tools\`.
3. Elige una de estas opciones:
   - añade esa carpeta al `PATH` de Windows
   - deja el ejecutable en su carpeta y selecciónalo manualmente al abrir la app por primera vez

### 3. Comprobación al iniciar

La aplicación ahora comprueba `zimdump` al arrancar.

- Si `zimdump` está disponible, puedes convertir archivos inmediatamente.
- Si falta, la aplicación muestra una advertencia y te permite buscar `zimdump.exe` en ese momento.
- La aplicación puede seguir abierta sin `zimdump`, pero la conversión y la extracción de metadatos seguirán bloqueadas hasta configurarlo.

### 4. Configurar la sincronización con WeKnora

El primer destino integrado para sincronización RAG es WeKnora.

En `WeKnora Sync Configuration` debes configurar:

- la URL base de WeKnora
- el modo de autenticación: `API Key` o `Bearer Token`
- el token de acceso
- el ID o el nombre de la base de conocimiento
- los IDs opcionales de modelos `KnowledgeQA`, `Embedding` y `VLLM`, obtenidos desde `/api/v1/models`
- si la aplicación puede crear la base automáticamente cuando el nombre no exista

La interfaz de sincronización permite:

- cargar las bases de conocimiento disponibles desde el servidor
- probar la conexión antes de sincronizar
- volver a aplicar los modelos configurados de chat, embedding y multimodal cuando se crea una base o se inicia una sincronización
- seleccionar salidas de conversión completadas para enviarlas a WeKnora
- revisar historial, registros, progreso, ETA y estado de pausa/reanudación

## Flujo de uso

1. Si usas el paquete publicado, instala primero .NET 8 Desktop Runtime; si trabajas desde el código fuente, instala .NET 8 SDK.
2. Asegúrate de tener `zimdump` instalado.
3. Inicia la aplicación.
4. En el primer arranque configura:
   - el directorio `kiwix-desktop`
   - el directorio de salida por defecto
   - opcionalmente, la ruta del ejecutable `zimdump`
5. Si la comprobación inicial indica que falta `zimdump`, corrige `PATH` o selecciona `zimdump.exe` manualmente.
6. Haz clic en `Scan ZIM Files` para sincronizar los archivos locales.
7. Selecciona un ZIM en la lista y, si lo necesitas, define una carpeta de salida específica para esa tarea.
8. Inicia la conversión y sigue el progreso, el historial y los registros desde la interfaz.
9. Para enviar artículos a WeKnora, abre `WeKnora Sync`, selecciona una o varias conversiones completadas y crea una tarea de sincronización.

## Automatización de CI y releases

- [`.github/workflows/ci.yml`](.github/workflows/ci.yml) compila el proyecto y sube un artefacto en cada push a `main` y en cada pull request.
- [`.github/workflows/release.yml`](.github/workflows/release.yml) ahora publica una GitHub Release automática en cada push a `main`, calculando el siguiente patch semántico a partir de la última etiqueta publicada.
- El mismo workflow también admite `workflow_dispatch` para sobrescribir manualmente la versión cuando sea necesario.
- [`.github/release.yml`](.github/release.yml) define la estructura de las release notes generadas automáticamente.

## Fuentes de la wiki

- Las páginas multilingües de la wiki se guardan en [docs/wiki](docs/wiki).
- Actualmente incluyen la página principal y el proceso de release en inglés, chino, japonés, español y árabe.

## Licencia

Este proyecto se distribuye bajo la licencia MIT. Consulte [LICENSE](LICENSE).