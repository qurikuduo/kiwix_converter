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
- .NET 8 Desktop Runtime para ejecutar la aplicación empaquetada
- .NET 8 SDK si quieres compilar el proyecto desde el código fuente
- `zimdump` disponible en `PATH` o configurado desde la interfaz

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