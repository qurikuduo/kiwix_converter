# Arquitectura de Kiwix Converter

## Objetivos de diseño

- Convertir archivos ZIM en Markdown por artículo y artefactos JSON listos para RAG.
- Mantener operativa la interfaz de escritorio incluso cuando las tareas largas se pausan o se interrumpen.
- Persistir suficiente estado en SQLite para reanudar el trabajo con granularidad por artículo.
- Tratar `zimdump` y la API HTTP de WeKnora como límites externos explícitos, en lugar de reimplementar esos protocolos en la capa de UI.

## Estructura por capas

- `KiwixConverter.WinForms`: shell de escritorio, configuración, tablas de tareas, superficies de estado y acciones del operador.
- `KiwixConverter.Core.Infrastructure`: rutas de la aplicación, valores JSON por defecto y lógica de repositorio/inicialización SQLite.
- `KiwixConverter.Core.Models`: configuración, filas ZIM, tareas, checkpoints, metadatos, filas de sincronización y logs.
- `KiwixConverter.Core.Conversion`: subprocesos `zimdump`, extracción del contenido principal, conversión a Markdown, fragmentación y reescritura de rutas.
- `KiwixConverter.Core.Services`: orquestación del escaneo, coordinación de conversión, acceso a la API de WeKnora y ejecución de sincronización reanudable.

## Estado persistente

- `settings` guarda directorios, `zimdump` y la configuración de sincronización con WeKnora.
- `zim_library` guarda el inventario local de archivos ZIM.
- `conversion_tasks` y `article_checkpoints` guardan el progreso de exportación y su estado de reanudación.
- `weknora_sync_tasks` y `weknora_sync_items` guardan el progreso de subida y su estado de reanudación.
- `log_entries` y `weknora_sync_log_entries` guardan el historial operativo consultable.

## Flujo de conversión

1. Escanear el directorio configurado de kiwix-desktop y hacer upsert de los ZIM encontrados en la base de datos.
2. Usar `zimdump` para obtener metadatos del archivo, listas de artículos, HTML y recursos relacionados.
3. Extraer el cuerpo principal, normalizar enlaces e imágenes y convertir el fragmento HTML limpio a Markdown.
4. Generar `content.md`, `metadata.json`, `chunks.jsonl` y carpetas de imágenes para cada artículo.
5. Persistir checkpoints por artículo y heartbeat de la tarea para reintentar solo el tramo local que falló.

## Flujo de sincronización con WeKnora

1. Cargar bases de conocimiento desde el servidor de WeKnora y permitir reutilizar o crear una nueva.
2. Cargar IDs vivos de modelos `KnowledgeQA`, `Embedding` y `VLLM` desde `/api/v1/models`.
3. Crear KB con la descripción, chunk size, chunk overlap y ajuste parent-child elegidos por el usuario.
4. Aplicar los IDs de modelo seleccionados a la inicialización de la KB antes de subir contenido.
5. Subir el Markdown de cada artículo como conocimiento manual y guardar checkpoints de sincronización reanudables.

## Notas de ejecución

- El release actual de Windows es framework-dependent y requiere .NET 8 Desktop Runtime.
- Los builds locales desde código fuente requieren .NET 8 SDK.
- La aplicación tolera varios formatos de salida de `zimdump`, pero sigue asumiendo que existe un `zimdump.exe` funcional.