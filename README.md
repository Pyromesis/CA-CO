# CA-CO

> ⚠️ **Proyecto en desarrollo (Fase 0).** Base funcional y extensible de la aplicación;
> muchas secciones de Configuración aparecen como *próximamente* a propósito.

**CA-CO** es una aplicación de escritorio **nativa de Windows** y **offline-first**:
tu espacio personal para almacenar, organizar, visualizar y administrar documentos
y notas en local, sin nube ni cuentas.

Tipos soportados en Fase 0: **PDF, PNG, JPG, JPEG, DOCX, XLSX, TXT**.

## Características actuales (Fases 0–1 + espacio de trabajo)

- **Espacio de trabajo integrado**: clic en un documento para leerlo dentro de CA-CO —
  PDF con el visor integrado (zoom, buscar), imágenes con zoom y **tinta**
  (lápiz, resaltador, guardar anotación como documento nuevo), TXT **editable**
  con guardado en la biblioteca. Word/Excel se abren con su app externa.
- **Comentarios por documento** con **dictado por voz** del sistema (offline si el
  paquete de voz español está instalado; requiere micrófono).
- Shell Fluent (Mica, beige/café) con Inicio, Documentos, Cuadernos, Favoritos,
  Recientes, Papelera y Configuración.

- Shell Fluent (Mica) con Inicio, Documentos, Cuadernos, Favoritos, Recientes, Papelera y Configuración.
- **Detalle de documento**: vista previa de imágenes y texto, apertura con la app
  predeterminada, mostrar en Explorador, metadatos, hash, mover entre cuadernos y etiquetas.
- Importación individual, múltiple, por **carpeta** (recursiva) y por
  **arrastrar y soltar**, con **deduplicación por SHA-256**, miniaturas,
  **límites anti-DoS** (100 archivos / 1 GB por lote), **progreso con
  cancelación** e **informe** (importados · duplicados · errores con motivo).
- Cuadernos jerárquicos (árbol, anti-ciclos, borrado seguro sin pérdida),
  con columna **Sin clasificar** y **arrastrar y soltar** (archivos entre
  cuadernos; archivos y carpetas desde el Explorador). Cada columna tiene
  **casillas** con añadir, seleccionar todo/nada y eliminar por lote.
- Borrado con confirmaciones: una para la papelera ("quedará en la papelera"),
  doble para lo permanente ("de forma permanente").
- Favoritos, papelera con restaurar/vaciar (borrado físico completo), búsqueda por nombre.
- **SQLite** como persistencia (`Database/caco.db`, WAL, índices), con migración
  automática única desde el JSON de Fase 0.
- Biblioteca local auto-creada al primer arranque, reubicable a cualquier carpeta
  elegida (con acceso persistente) o a la ubicación interna por defecto.
- Logging a archivo, manejo centralizado de errores, ajustes persistentes.
- **Actualizaciones integradas**: Configuración → Actualizaciones comprueba
  GitHub Releases, descarga el instalador con progreso, verifica su firma
  y actualiza reabriendo la app.
- 74 pruebas automatizadas.

## Tecnologías

- **.NET 10** · **C#** · **WinUI 3** · **Windows App SDK 2.4** · XAML · MVVM
  (CommunityToolkit.Mvvm) · DI (Microsoft.Extensions.DependencyInjection) · xUnit.

## Arquitectura

```text
CA-CO
├── CA-CO.sln
├── src
│   ├── CA-CO.App            # WinUI 3: shell, páginas, ViewModels, servicios UI
│   ├── CA-CO.Core           # Result/Error, Guard, IClock, paginación
│   ├── CA-CO.Domain         # Entidades (sin dependencias de infraestructura)
│   ├── CA-CO.Application    # Casos de uso, puertos y abstracciones futuras
│   ├── CA-CO.Infrastructure # JSON, archivos, logging, DI
│   └── CA-CO.Tests          # xUnit (44 pruebas)
├── docs                     # Decisiones de arquitectura
└── assets                   # Recursos gráficos
```

> Los namespaces son `CaCo.*` (C# no admite guiones); carpetas y solución
> mantienen `CA-CO.*`. Detalles en [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Requisitos

- Windows 10 1809+ (diseño objetivo: Windows 11).
- [.NET 10 SDK](https://dotnet.microsoft.com/download).
- Visual Studio 2022 17.14+ / 2026 con la carga de trabajo
  **Desarrollo de aplicaciones WinUI** (o `dotnet build`).
- Modo de desarrollador activado para ejecutar empaquetado (`ms-settings:developers`).

## Cómo ejecutar

```powershell
# Clonar y compilar
git clone <url> CA-CO
cd CA-CO
dotnet build CA-CO.sln -c Debug -p:Platform=x64

# Pruebas
dotnet test src/CA-CO.Tests/CaCo.Tests.csproj

# Ejecutar (recomendado: F5 en Visual Studio sobre CaCo.App)
dotnet run --project src/CA-CO.App/CaCo.App.csproj

# Sin consola: doble clic en "CA-CO" del Escritorio (usa Iniciar-CA-CO.cmd)
```

La primera ejecución crea la biblioteca dentro de los datos de la app
(reubicable en Configuración → Almacenamiento a cualquier carpeta elegida;
los cambios de ubicación se aplican al reiniciar).

## Roadmap

```text
FASE 0 Base ✅ → FASE 1 Almacenamiento y gestión ✅ → 2 Importación avanzada ✅ →
3 PDF e imágenes ✅ → 4 Notas ✅ → 5 OCR ✅ → 6 Búsqueda avanzada ✅ → 7 Voz →
8 Seguridad y cifrado → 9 Backups → 10 Nube → 11 IA
```

Las abstracciones de OCR, voz, seguridad, nube e IA ya existen como interfaces
(`CaCo.Application.Future.*`) sin implementación.

## Licencia

MIT — ver [LICENSE](LICENSE).
