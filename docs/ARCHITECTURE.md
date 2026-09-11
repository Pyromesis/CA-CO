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
