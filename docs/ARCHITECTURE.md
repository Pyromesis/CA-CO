# CA-CO — Decisiones de arquitectura (Fase 0)

> Proyecto en desarrollo. Este documento registra las decisiones técnicas de la
> base para que las fases siguientes no tengan que redescubrirlas.

## 1. Stack

| Decisión | Valor | Motivo |
|---|---|---|
| .NET | 10 | Versión obligatoria de Fase 0, LTS moderno |
| UI | WinUI 3 + Windows App SDK **2.4.0** | Nativo Windows, Fluent, Mica. La versión 2.4 existe (línea mayor 2.x, 2026); se usa exacta |
| TFM App | `net10.0-windows10.0.26100.0`, mín. `10.0.17763.0` | Diseño objetivo Windows 11 sin bloquear equipos de desarrollo con Windows 10 |
| MVVM | CommunityToolkit.Mvvm 8.4.0 (estilo clásico `[ObservableProperty]`) | Estándar de facto; mismo estilo que la plantilla oficial `winui-mvvm` |
| DI | Microsoft.Extensions.DependencyInjection 10 | Sin framework pesado; un Composition Root en `App` + `AddCaco()` compartido con tests |
| Logging | Microsoft.Extensions.Logging + proveedor de archivo propio | `FileLoggerProvider` en `%LOCALAPPDATA%\CA-CO\logs`, rotación 5 MB, sin telemetría |
| Persistencia | SQLite (`Database/caco.db`), migración única desde JSON | Rápida, indexada, apta para búsquedas futuras; JSON queda como respaldo/tests |
| Tests | xUnit | 53 pruebas (Fase 1) |

## 2. Nombres: `CA-CO` vs `CaCo.*`

C# no admite guiones en identificadores. Solución:

- **Carpetas, solución y ensamblados visibles**: `CA-CO.*` (fiel a la especificación).
- **Namespaces y tipos**: `CaCo.*` (`CaCo.App`, `CaCo.Domain`, …).

## 3. Capas y dependencias

```text
CaCo.App ──▶ Application ──▶ Domain ──▶ Core
   │               ▲                        ▲
   └────▶ Infrastructure ───────────────────┘
```

- **Core**: `Result`/`Error`, `Guard`, `IClock`, `PagedResult`. Sin dependencias.
- **Domain**: entidades con lógica (`Document`, `Notebook`, `Note`, `Tag`,
  `DocumentMetadata`, `DocumentType`). Solo depende de Core. Expone `Rehydrate`
  interno (vía `InternalsVisibleTo`) para que Infrastructure reconstruya
  entidades sin romper encapsulación.
- **Application**: puertos (`IDocumentRepository`, `INotebookRepository`, …),
  casos de uso (`DocumentService`, `NotebookService`, `LibraryService`) y
  abstracciones futuras (`Future/`). No conoce WinUI ni `System.IO` salvo tipos.
- **Infrastructure**: adaptadores (JSON, archivos, FileLogger, `AddCaco()`).
- **App**: solo WinUI — páginas, ViewModels, `NavigationService`,
  `DialogService`, `FilePickerService`. La lógica vive en Application.

Regla: **ninguna ruta absoluta se persiste**. Los documentos guardan
`StoredFileName`; la ruta se reconstruye con `ILibraryPaths` → la biblioteca
es reubicable.

## 4. Decisiones de dominio relevantes

- **Borrado lógico**: `IsDeleted` + papelera con restaurar/vaciar. El borrado
  físico solo ocurre al vaciar o eliminar definitivamente.
- **Eliminar cuaderno nunca pierde contenido**: los hijos suben al abuelo y los
  documentos se desclasifican (siguen en Documentos).
- **Anti-ciclos**: la entidad impide auto-parentesco; el servicio recorre
  ancestros y rechaza ciclos (`Notebook.Cycle`, probado).
- **Duplicados Fase 0**: mismo nombre + tamaño + cuaderno. Fase 1: hash SHA-256
  (`Document.ContentHash` ya reservado).
- **Metadatos extensibles**: `DocumentMetadata` (clave/valor) es el punto de
  extensión para OCR, IA y nube sin cambiar el esquema.

## 5. UI / MVVM

- Navegación **ViewModel-first**: `INavigationService` con mapa VM → Page.
  Añadir secciones = registrar un par + un `NavigationItem`.
- Páginas resuelven su VM del contenedor (`PageBase<T>`); el code-behind solo
  enlaza UI (DnD, `ContainerContentChanging` para `DataTemplate`, que es donde
  `x:Bind` no alcanza al VM).
- Errores: `Error` técnico → log → `IErrorHandler` → `InfoBar` entendible.
  Nunca stack traces al usuario. `App.UnhandledException` evita cierres.
- Listas paginadas (“Cargar más”): la biblioteca completa nunca se carga en memoria.
- Compilación **0 errores / 0 warnings** en Debug x64 y 0 errores en Release x64
  (Release muestra 2 avisos IL2104 del propio SDK de Windows, ajenos al proyecto;
  la serialización propia usa source generation trim-safe).

## 6. Puertos futuros (solo interfaces, Fase 0)

| Interfaz | Fase |
|---|---|
| `IOcrService`, `IOcrEngine` | 5 |
| `ISpeechToTextService` | 7 |
| `ISecurityService`, `IEncryptionService`, `IAuthenticationService` | 8 |
| `ICloudStorageProvider` (OneDrive/Drive/Dropbox por DI) | 10 |
| `IAiService` (local o API) | 11 |

## 7. Rutas

- Biblioteca por defecto: `LocalFolder` del paquete (`…\Packages\<familia>\LocalFolder\CA-CO\Library`):
  ruta real sin virtualización para que otras apps puedan abrir los archivos.
  `LibraryRootResolver.ResolveEffective` la aplica si la configurada falla la sonda.
- Migración legada única (`LegacyVfsMigrator`) desde la ruta virtualizada anterior.
- Carpeta elegida: selector del sistema + token FutureAccessList persistente
  (`CacoSettings.LibraryToken`), resuelto al arrancar (`BrokeredFolder`).
- Ajustes: `%LOCALAPPDATA%\CA-CO\settings.json`. Logs: `…\logs\caco-AAAAMMDD.log`.
- Limitación conocida: desinstalar la app puede borrar la biblioteca interna;
  para durabilidad, elige una carpeta propia (Fase 9: backups).

## 8. Delta Fase 1 (gestión documental)
- Persistencia: **SQLite** (`Database/caco.db`, WAL, índices por cuaderno, fecha,
  nombre y hash) con migración única desde JSON (`JsonToSqliteMigrator`, idempotente).
- Deduplicación por **SHA-256** (`Document.ContentHash`, `FindByHashAsync`).
- Copia original rastreada (`OriginalStoredFileName`); el borrado definitivo
  elimina administrada + original + miniatura.
- Detalle con vista previa (imagen/texto), apertura externa (`Launcher`),
  etiquetas gestionadas y mover entre cuadernos (navegación con parámetro).
- Miniaturas de imágenes generadas al importar (`IThumbnailService` en capa UI).
- Serialización JSON por generación de código (trim-safe para Release).

## 9. Espacio de trabajo (Fase 3, primera parte)

- Reader (documento a la izquierda, comentarios a la derecha): PDF con WebView2
  (visor Edge con zoom y búsqueda propios), imágenes con zoom y tinta vectorial
  propia (lápiz, resaltador, deshacer; WinUI 3 no incluye InkCanvas), TXT editable
  con guardado (tamaño y hash recalculados), Office mediante app externa.
- Comentarios = notas vinculadas (`INoteService`). Dictado por voz con el
  reconocedor del sistema y capability de micrófono (offline con paquete de voz).
- La tinta se guarda como documento nuevo reutilizando el importador.
- Estrellas y glifos de fila por código (determinista ante reciclaje).
- OCR local de imágenes (`IImageOcrService` con Windows.Media.Ocr): copiar,
  guardar como comentario o como TXT nuevo. Dictado con SpeechRecognizer.
- `RefreshFileMetadataAsync` adopta ediciones externas (Ctrl+S en el visor).

## 10. Delta Fase 2 (importación avanzada)

- `IBatchImportService` (`ImportBatchOptions`: 100 archivos / 1 GB): valida
  límites ANTES de empezar (rechazo previo sin importar nada), informa
  `IProgress<ImportProgress>`, no aborta ante fallos individuales y devuelve
  `ImportBatchResult` (importados · duplicados · fallidos con motivo ·
  `WasCancelled`). Los motivos pasan por `IErrorHandler` (mensajes entendibles).
- `IFolderScanner`: recursivo opcional, solo extensiones soportadas (vía
  `SupportedFileTypes.IsSupported`, que ahora acepta rutas completas —
  corrección: el drag & drop filtraba todo), salta ocultos/sistema, orden
  determinista, tope de 5000 entradas, tolera carpetas inaccesibles.
- UI: Documentos suma "Importar carpeta" (usa `IFolderPickerService`),
  barra de progreso con "n/m · archivo" y botón Cancelar (CTS propio);
  informe final en InfoBar + diálogo con los primeros 10 motivos.
  Inicio y arrastrar/soltar reutilizan el lote (límites e informe gratis).

## 11. Delta Fase 3 (PDF e imágenes)

- `IMediaInspector` (`Application.Storage`, `MediaInfo`, `MediaMetadataKeys`):
  páginas PDF vía `Windows.Data.Pdf` y dimensiones vía `BitmapDecoder`
  (verificado unpackaged: conteo, render y dimensiones funcionan sin identidad
  de paquete). Implementación real en App (`MediaInspector`), `NullMediaInspector`
  en Infrastructure para tests/DI base (App la sustituye en `AddCacoUi`).
- El importador guarda `pdf.pageCount` / `image.width` / `image.height` en
  `DocumentMetadata` (best-effort: un fallo nunca tumba la importación; solo
  aplica al tipo correspondiente).
- `ThumbnailService` genera miniatura de la primera página del PDF (mismo
  conducto 256 px, tope 50 MP); el resto cae a icono con log.
- `DocumentFormat.Subtitle` añade "N págs." o "WxH" cuando hay metadatos.

## 12. Delta Fase 4 (notas)

- Notas sueltas con entidad propia: `INoteRepository.ListStandaloneAsync`
  (JSON + SQLite, `documentId/notebookId IS NULL`, tope 100) y
  `NoteService.AddStandaloneAsync/RenameAsync` (validación vía `Note`).
- Sección Notas en el shell (`NotesPage` + `NotesViewModel`: crear con título
  y contenido, renombrar, editar, eliminar con confirmación).
- Markdown ligero (`Application/Notes/MarkdownLite`, sin dependencias):
  `#/##/###`, `**negrita**`, `*cursiva*`, `` `código` ``, listas `-/*`;
  sin anidado ni escapes (lo sin cerrar sale literal), tope 500 bloques.
  La UI lo dibuja con `RichTextBlock` (`MarkdownRichText`); los comentarios
  del lector lo usan automáticamente.

## 13. Delta Fase 5 (OCR)

- `IImageOcrService.RecognizePdfAsync` (App): motor del sistema por página
  (render `Windows.Data.Pdf` + `OcrEngine`), tope 20 páginas y 50 MP por
  página renderizada, páginas ilegibles saltadas, progreso `OcrProgress`
  y cancelación entre páginas. Verificado en vivo: motor es-ES disponible,
  "HOLA MUNDO 123" leído exacto, PDF en blanco → `Ocr.NoText` con gracia.
- Lógica pura en `Application/Ocr` (`PdfOcrOptions`, `OcrPages.TakePages`,
  `Combine` con separador determinista y tope duro, `OcrMetadataKeys`).
- `DocumentService.SetOcrResultAsync` persiste `ocr.done/pages/chars/excerpt`
  (extracto ≤1000) como corpus para la búsqueda de Fase 6.
- Lector: botón "Extraer texto (OCR)" también en PDFs, con barra de progreso
  ("Página 3/12…"); al terminar ofrece copiar/comentario/TXT y guarda el OCR.

## 14. Delta Fase 6 (búsqueda avanzada)

- `AdvancedSearchService` (sustituye a `BasicSearchService`): multi-campo con
  relevancia (nombre 100/70, archivo 65, etiqueta 60, nota 50, OCR 40, filtro 10),
  orden por puntuación y fecha, tope de 2000 documentos y 5000 notas en memoria
  (escala personal, idéntico en JSON y SQLite, sin dialectos).
- `SearchQuery` suma `FileType?`, `FavoritesOnly`, `Tag?` y rango de fechas;
  `SearchHit` explica cada resultado (`MatchedIn`: nombre/archivo/etiqueta/nota/OCR).
- `INoteRepository.ListAllAsync` (JSON + SQLite) para el corpus de notas.
- UI: Documentos filtra por tipo y favoritos y muestra "N resultados ·
  coincide en: …"; Notas busca en título y contenido en local.

## 15. Endurecimiento (clic abre documento, OCR, DnD, confirmaciones)

- Causa del clic muerto: `ReaderPage` tocaba `PdfView` (con `x:Load`) en el
  constructor y lanzaba `NullReferenceException`, tragada por `UnhandledException`
  (visible en `%LOCALAPPDATA%\CA-CO\logs`). Regla: elementos con `x:Load`
  solo se tocan en sus propios eventos (`Loaded="PdfView_Loaded"` + bandera
  anti-doble). El mismo fallo bloqueaba el OCR (vive dentro del lector).
- Cuadernos: columna Sin clasificar (`UnclassifiedOnly`), arrastre interno
  (`CA-CO-DOCS:` por texto) y del Explorador (archivos + carpetas vía
  `IFolderScanner` + lote con cuaderno destino); soltar en Sin clasificar
  desclasifica. `MoveDocumentsToNotebookAsync` no aborta al primer fallo.
- Selección por casillas (`DocumentRow`, `NotebookRow.IsSelected`) con acciones
  por columna: añadir, seleccionar todo/nada y borrados por lote (cuadernos de
  dentro hacia fuera, documentos a la papelera con una confirmación).
- Confirmaciones: papelera una ("Quedará en la papelera"), permanente doble
  ("de forma permanente"): cuadernos, borrado definitivo, vaciar papelera
  y notas (sin papelera).

## 16. Hilos UI, indentación y OCR legible

- `RPC_E_WRONG_THREAD` al guardar OCR: varios `await …ConfigureAwait(false)`
  en ViewModels continuaban en pool y tocaban propiedades bindeadas.
  Regla en `ViewModelBase`: prohibido `ConfigureAwait(false)` en
  ViewModels y code-behind (8 retirados en Reader/Detalle).
- Sangría del árbol por `Margin` (`IndentMargin`), no por espacios
  (XAML los colapsa y la jerarquía se veía rota).
- OCR multipágina con cabeceras "— Página N —" (`PageText`); el instalador
  lleva `restartreplace` para archivos bloqueados.

## 17. Barrido total (hilos, diálogos, voz, textos)

- Lectura TXT en bucle hasta EOF (un parcial se guardaba encima del archivo).
- `ViewModelBase.ShowError` ignora `OperationCanceledException`: la cancelación
  rutinaria ya no pinta errores; los `CancellationToken.None` de recargas pasaron a `ct`.
- `DialogService` serializa `ContentDialog` con semáforo y vuelve al hilo UI
  (doble clic ya no revienta con doble diálogo).
- Voz: `StopAsync` honesto, `ct` en creación, bandera anti-doble `StartVoice`,
  temporales del instalador de idioma con limpieza, cancelado como aviso Info.
- Arranque con try, `IsBusy` en cuadernos, token único FutureAccessList,
  logs en `FileLauncherService`, sonda de escritura al guardar ruta, recargas
  que no tapan errores, `temp` en `finally`, reciclaje de comentarios.
- Limpieza de mojibake (tildes corruptas heredadas + algunas del transporte):
  sangría por margen, OCR por páginas y censo de caracteres raros a cero.

## 18. Delta Fase 7 (voz)

- Lectura en voz alta (`IVoiceReaderService` con `SpeechSynthesizer` +
  `MediaPlayer`, sin elemento UI): voz es-ES preferida (aquí "Microsoft Pablo"),
  troceado por frases (`SpeechChunking`, 4000 caracteres, testado), parar al
  navegar/recargar, nombre de la voz visible. Verificado en vivo: sintetiza
  y reproduce, vacío → `Voice.EmptyText` limpio.
- El dictado ya existía; se suma `IsNotReading`/botones Escuchar/Detener.

## 19. Delta Fase 8 (bloqueo PIN + Hello)

- PIN con PBKDF2-SHA256 (210k iteraciones, sal de 16 B, comparación en
  tiempo constante) y espera de 30 s tras 5 fallos (`PinLockService`, reloj
  inyectable y testeado, incluido roundtrip por JSON).
- Desbloqueo con Windows Hello si está disponible (verificado unpackaged:
  `CheckAvailabilityAsync` = Available); el gate pide Hello y cae al PIN.
- Puerta al arrancar (`MainWindow.OnFirstActivated`): sin desbloqueo se cierra.
- Ajustes/Seguridad real: establecer, cambiar y desactivar PIN (con verificar
  el actual), activar/desactivar Hello (exige Hello verificado) y sonda de
  escritura al guardar ruta. Cifrado de archivos: diseñado para Fase 8B.
