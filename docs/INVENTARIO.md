# CA-CO — Inventario del repo y estado real

Fecha: 2026-09-11 · Versión registrada: **1.1.0** · Tests declarados: **74** (verificados: 74 passed)

Fuentes: `CA-CO.sln`, `Directory.Build.props`, `Directory.Packages.props`,
`README.md`, `docs/ARCHITECTURE.md`, `src/`, `installer/`, `assets/`, `docs/`.
Build verificado: `dotnet build CA-CO.sln -c Debug -p:Platform=x64` → 7 proyectos (6 + carpeta `src`), 0 errores, 0 warnings.
Tests verificados: `dotnet test src/CA-CO.Tests/CaCo.Tests.csproj` → 74 passed.

## 1. Versión

- `Directory.Build.props`: `<Version>1.1.0</Version>`, `<FileVersion>1.1.0</FileVersion>`, `<InformationalVersion>1.1.0</InformationalVersion>`.
- `installer/CA-CO.iss`: `#define AppVersion "1.1.0"`.
- `installer/Build-Installer.ps1`: `$version = '1.1.0'`.

## 2. Proyectos (6)

| Proyecto (carpeta `src/`) | Assembly / namespace | Rol |
|---|---|---|
| `CA-CO.App` (`CaCo.App.csproj`) | `CaCo.App` | WinUI 3: shell, páginas, ViewModels, servicios UI |
| `CA-CO.Core` (`CaCo.Core.csproj`) | `CaCo.Core` | `Result`/`Error`, `Guard`, `IClock`, `PagedResult` |
| `CA-CO.Domain` (`CaCo.Domain.csproj`) | `CaCo.Domain` | Entidades `Document`, `Notebook`, `Note`, `Tag`, `DocumentMetadata`, `DocumentType` |
| `CA-CO.Application` (`CaCo.Application.csproj`) | `CaCo.Application` | Puertos, casos de uso, `Future/*`, Updates, Storage, Search, Import, Configuration |
| `CA-CO.Infrastructure` (`CaCo.Infrastructure.csproj`) | `CaCo.Infrastructure` | SQLite + JSON, archivos, logging, `AddCaco()`, GitHub Updates |
| `CA-CO.Tests` (`CaCo.Tests.csproj`) | `CaCo.Tests` | xUnit |

`CA-CO.sln` declara los 6 anteriores + carpeta virtual `src`.

## 3. `src` — conteos

- Páginas XAML (`src/CA-CO.App/Pages/`): **9**
  `DocumentDetailPage`, `DocumentsPage`, `FavoritesPage`, `HomePage`,
  `NotebooksPage`, `ReaderPage`, `RecentsPage`, `SettingsPage`, `TrashPage`
  (+ `PageBase.cs`, `DocumentListHelper.cs` como code-behind de apoyo).
- Vistas compartidas (`src/CA-CO.App/Views/`): 3 XAML
  `DocumentItemView`, `RecentDocumentView`, `CommentView` (+ `DocumentFormat.cs`).
- ViewModels (`src/CA-CO.App/ViewModels/`): **7 ficheros**
  `ViewModelBase.cs` (base) + 6 concretos:
  `DocumentDetailViewModel`, `DocumentListViewModels.cs` (base abstracta `DocumentListViewModel` + `Documents/Favorites/Recents/TrashViewModel`),
  `HomeViewModel`, `NotebooksViewModel`, `ReaderViewModel`, `SettingsViewModel`.
- Servicios UI (`src/CA-CO.App/Services/`): **11**
  `NavigationService`, `DialogService`, `UiServices`, `FilePickerService`,
  `FolderPickerService`, `FileLauncherService`, `ThumbnailService`,
  `VoiceDictationService`, `VoiceMessageSession`, `ImageOcrService`, `LanguageFeatureInstaller`.
- `CA-CO.Core`: 4 ficheros (`Result`, `Error`, `Guard`, `Clock`).
- `CA-CO.Domain`: 7 entidades/ficheros + `AssemblyInfo` (`InternalsVisibleTo Infrastructure`).
- `CA-CO.Application`: `Services/` (4), `Repositories/` (3), `Future/` (5: `Ocr`, `Speech`, `Security`, `Cloud`, `Ai`), `Updates/` (3), `Storage/` (2), `Configuration/`, `Search/`, `Import/`, `Errors/`.
- `CA-CO.Infrastructure`: SQLite (`SqliteDatabase`, `SqliteDocument/Notebook/NoteAndTagRepositories`, `JsonToSqliteMigrator`), JSON legado (4 + `Dtos`, `CacoJsonContext`, `JsonFileStore`), `Storage/`, `Configuration/` (`StorageProbe`, `LegacyVfsMigrator`), `Search/BasicSearchService`, `Updates/GitHubUpdateService`, `Logging/FileLogger`, `Import/LocalDocumentImporter`, `Errors/AppErrorHandler`, `DependencyInjection/` (`CacoServices`, `NullThumbnailService`).
- Tests (`src/CA-CO.Tests/`): 11 `.cs` útiles + 3 generados en `obj/`:
  `Core/ResultTests`, `Domain/DocumentTests, NotebookTests, SupportedFileTypesTests`,
  `Application/Phase1Tests, ServiceAndImportTests, ReaderWorkspaceTests, UpdateVersionTests`,
  `Infrastructure/SqliteRepositoryTests, StorageAndRepositoryTests`, `Helpers/TestHelpers`.

## 4. Tests: 74 declarados = 74 ejecutados

- Atributos: **56** (`[Fact]`/`[Theory]`) en 10 ficheros.
- `InlineData`: **23** que expanden 5 `[Theory]`:
  - `SupportedFileTypesTests`: 8 + 5 casos + 2 Facts = 15
  - `DocumentTests`: 2 casos + 6 Facts = 8
  - `UpdateVersionTests`: 3 + 5 casos + 3 Facts = 11
  - Resto Facts: `StorageAndRepository` 5 + `Sqlite` 3 + `Notebook` 6 + `Result` 4 + `Phase1` 8 + `ReaderWorkspace` 8 + `ServiceAndImport` 6 = 40
  - Total: 15 + 8 + 11 + 40 = **74**.
- `README.md` declara “74 pruebas automatizadas” (correcto).
- Desfase conocido: el diagrama de `README.md` aún dice “44 pruebas” y `docs/ARCHITECTURE.md §1` dice “53 pruebas (Fase 1)” — ambos desactualizados.

## 5. `docs/` (1 + este archivo)

- `docs/ARCHITECTURE.md` (122 líneas): stack (.NET 10, WinUI 3 + WASDK 2.4.0, CommunityToolkit.Mvvm 8.4.0, DI 10, SQLite WAL), regla `CA-CO.*` vs `CaCo.*`, capas, borrado lógico, anti-ciclos, puertos futuros, rutas, deltas Fase 1 y espacio de trabajo (Reader/WebView2, tinta propia, OCR `Windows.Media.Ocr`).
- `docs/INVENTARIO.md` (este archivo).

## 6. `installer/` (3 ficheros + 2 carpetas)

- `installer/CA-CO.iss`: Inno Setup per-usuario (`PrivilegesRequired=lowest`, `{localappdata}\Programs\CA-CO`), `AppVersion 1.1.0`, salida `dist/CA-CO-Setup-1.1.0-x64.exe`, firma `casign` (SignTool), chequeo WebView2/Edge, `Tasks deletedata` opcional, limpieza de carpeta huérfana al cambiar destino.
- `installer/Build-Installer.ps1`: pipeline publish Release autocontenida → firma `signtool` (thumbprint `F9EF…`) → `ISCC.exe`.
- `installer/CA-CO-CA.crt`: CA local para firma.
- `installer/dist/`, `installer/staging/`: salidas generadas (no versionar artefactos).

## 7. `assets/` (1)

- `assets/caco-icon.svg` (único recurso gráfico versionado).
- Nota: `installer/CA-CO.iss` referencia `../src/CA-CO.App/Assets/AppIcon.ico` (icono empaquetado, fuera de `assets/`).

## 8. TODOs localizados: 0

- Búsqueda `TODO|FIXME|HACK|XXX` en `src/` (grep + `Get-ChildItem … | Select-String`): **0 coincidencias**.
- `throw new NotImplementedException`: **0** en `src/` (los futuros son solo interfaces en `CaCo.Application.Future.*`, sin stubs que lancen).
- Conclusión: no hay deuda marcada con TODOs; la deuda está documentada en prosa (ver §10).

## 9. “Próximamente” (5 localizaciones en código + README)

1. `src/CA-CO.App/Pages/SettingsPage.xaml:143` — `Seguridad (próximamente)` (PIN/Hello + cifrado, `IsEnabled=False`).
2. `src/CA-CO.App/Pages/SettingsPage.xaml:155` — `OCR y voz (próximamente)` (OCR auto + dictado, deshabilitados).
3. `src/CA-CO.App/Pages/SettingsPage.xaml:167` — `Nube (próximamente)` (OneDrive/Drive/Dropbox, deshabilitado).
4. `src/CA-CO.Application/Configuration/AppConfiguration.cs:5` — comentario: secciones futuras existen para mostrarse como “próximamente” (`Security/Ocr/Voice/Cloud.Available=false`).
5. `src/CA-CO.App/ViewModels/SettingsViewModel.cs:13` — summary: “ajustes reales + secciones futuras como próximamente”.
- `README.md:3-4` y `docs/ARCHITECTURE.md §6`: intencional desde Fase 0; puertos `IOcrService`, `ISpeechToTextService`, `ISecurity/IEncryption/IAuthenticationService`, `ICloudStorageProvider`, `IAiService` solo como interfaces.

## 10. Deuda técnica y estado real

1. Docs desfasadas: `README` diagrama “44 pruebas” y `ARCHITECTURE §1` “53 pruebas” vs 74 reales; `ARCHITECTURE` titulado “Fase 0” pero ya describe Fase 1 + workspace.
2. Release x64: 2 avisos `IL2104` del propio Windows App SDK (documentados como ajenos; serialización propia ya es trim-safe con source generation).
3. Durabilidad: desinstalar puede borrar la biblioteca interna (`LocalFolder`); mitigado con carpeta reubicable + `Tasks deletedata` opcional, pendiente Fase 9 (backups).
4. Doble persistencia: SQLite es principal (`Database/caco.db`, WAL, índices), JSON queda como respaldo/tests + `JsonToSqliteMigrator` idempotente — mantener ambos hasta retirar legado.
5. Futuros sin implementar: Fases 2-11 (import avanzada, PDF/imágenes resto, notas, OCR auto, búsqueda avanzada, voz, seguridad/cifrado, backups, nube, IA) solo interfaces.
6. Limitaciones UI conocidas: tinta vectorial propia (WinUI 3 sin `InkCanvas`), TXT editable pero Office externo, dictado requiere micrófono + paquete voz ES, PDF depende de WebView2/Edge.
7. `assets/` mínimo (1 SVG); icono real vive en `src/CA-CO.App/Assets/`.

## 11. Cómo reverificar

```powershell
dotnet build CA-CO.sln -c Debug -p:Platform=x64
dotnet test src/CA-CO.Tests/CaCo.Tests.csproj
```
