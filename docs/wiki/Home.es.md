# Wiki de Kiwix Converter

Bienvenido a la wiki de Kiwix Converter.

## Resumen

Kiwix Converter transforma archivos ZIM de kiwix-desktop en Markdown por artículo, metadatos JSON y archivos JSONL por fragmentos para sistemas RAG.

## Áreas principales

- Interfaz de escritorio: configuración, escaneo, control de tareas, historial y registros
- Motor de conversión: integración con `zimdump`, extracción del contenido principal, conversión a Markdown, exportación de imágenes y reescritura de enlaces
- Persistencia: configuración, checkpoints, metadatos y logs almacenados en SQLite
- Automatización: CI, empaquetado y publicación de releases con versiones semánticas

## Arquitectura y flujo de datos

- `KiwixConverter.WinForms` gestiona la shell para operadores, mientras `KiwixConverter.Core` gestiona escaneo, conversión, sincronización y persistencia.
- El escaneo del directorio hace upsert del inventario ZIM en SQLite antes de comenzar la conversión.
- La conversión usa `zimdump` para obtener metadatos y HTML, y luego genera Markdown, metadatos JSON, chunks y carpetas de imágenes.
- La sincronización con WeKnora puede cargar IDs de modelos desde `/api/v1/models`, crear o reutilizar KB con configuración de chunk y subir Markdown por artículo con estado reanudable.

## Inicio rápido

Para usuarios no técnicos, la ruta más sencilla es:

1. Descargar el último Windows release zip.
2. Instalar .NET 8 Desktop Runtime.
3. Instalar `zimdump` y añadirlo a `PATH`, o seleccionar `zimdump.exe` desde la aplicación.
4. Abrir la aplicación, elegir el directorio de `kiwix-desktop` y la carpeta de salida, y luego escanear los archivos ZIM.

Si vas a compilar desde el código fuente, instala .NET 8 SDK.

## Comprobación de dependencias al iniciar

La aplicación de escritorio ahora verifica `zimdump` al arrancar.

- Si `zimdump` está disponible, la conversión queda lista.
- Si falta, la app muestra una advertencia y permite buscar el ejecutable de inmediato.
- La app puede seguir abierta sin `zimdump`, pero las funciones de exportación quedan bloqueadas hasta configurarlo.

## Sincronización con WeKnora

El primer destino integrado para RAG es WeKnora.

El flujo actual de escritorio admite:

- URL base y autenticación mediante `API Key` o `Bearer Token`
- carga de bases de conocimiento desde el servidor de WeKnora
- creación automática por nombre cuando está habilitada
- configuración de IDs de modelos `KnowledgeQA`, `Embedding` y `VLLM` obtenidos desde `/api/v1/models`
- reaplicación de los modelos configurados de chat, embedding y multimodal cada vez que se crea una base o se inicia una sincronización
- selección de conversiones completadas para sincronizar
- historial de sincronización, logs por tarea, progreso, ETA, pausa/reanudación y checkpoints reanudables

## Páginas por idioma

- [Home](Home)
- [首页（简体中文）](Home.zh-CN)
- [ホーム（日本語）](Home.ja)
- [Inicio (Español)](Home.es)
- [الصفحة الرئيسية (العربية)](Home.ar)

## Páginas adicionales

- [Arquitectura](Architecture)
- [Release Process](Release-Process)
- [发布流程（简体中文）](Release-Process.zh-CN)
- [リリース手順（日本語）](Release-Process.ja)
- [Proceso de release (Español)](Release-Process.es)
- [عملية الإصدار (العربية)](Release-Process.ar)

## Notas de instalación

- `zimdump` proviene de las herramientas de Kiwix; no está incluido en este repositorio.
- El release actual de Windows es framework-dependent, por lo que conviene instalar .NET 8 Desktop Runtime antes del primer arranque.
- Para compilar localmente sigue siendo necesario .NET 8 SDK.