# CLAUDE.md — Contexto del Proyecto

## Stack Tecnológico

- **Framework**: .NET 8
- **UI**: Blazor Server / Blazor WebAssembly con **MudBlazor**
- **Base de datos**: SQL Server 2019 (`CPDADMFA1`)
- **Acceso a datos**: `Microsoft.Data.SqlClient` (conexión directa, sin ORM)
- **Lenguaje**: C# 12+

## Estructura Real del Proyecto

```
C:\Programs\Quality Gates\
│
├── nuget.config                          → Fuentes NuGet locales (anula config global)
│
├── QUALITY_GATES\                        → Librería de lógica de negocio y datos
│   ├── QUALITY_GATES.csproj
│   ├── QUALITY_GATES.sln
│   ├── Class_SQL.cs                      → InternalsVisibleTo para tests
│   ├── ServiceCollectionExtensions.cs    → Registro DI: AddQualityGatesData()
│   │
│   ├── Classes\                          → Business Layer (clases parciales por área)
│   │   ├── Quality_Gates.cs              → Class_Projectos (partial) — constructor + _db
│   │   ├── Class_Projectos.Projects.cs   → partial — métodos de proyectos
│   │   └── Models.cs                     → Modelos: PROJECTS, PROJECT_GATES, GATES_ACTIONS, DELIVERABLES, DeliverableFile
│   │
│   └── Data\                             → Capa de acceso a datos
│       ├── IDbConnectionFactory.cs       → Interfaz + clase Return_SQL_Action
│       ├── SqlConnectionFactory.cs       → Implementación concreta
│       └── ConectionDescription.cs       → Descriptor de conexión alternativa
│
└── Tester Quality Gates\                 → App WinForms para pruebas
    ├── Tester Quality Gates.sln
    ├── Program.cs
    ├── Form1.cs
    └── Form1.Designer.cs
```

## Arquitectura de Acceso a Datos

### Interfaz `IDbConnectionFactory` — contrato completo

```csharp
// Namespace: QUALITY_GATES.Data

Task<SqlConnection> CreateOpenConnectionAsync(CancellationToken cancellationToken = default);

// SELECT — devuelve DataTable
Task<Return_SQL_Action> GetDatatableFromSelectAsync(
    string strSql,
    SqlParameter[]? parameters = null,
    CancellationToken cancellationToken = default);

// INSERT / UPDATE / DELETE — con soporte de transacción opcional
Task<Return_SQL_Action> NonQueryDataToSQLServer(
    string strNonQuerySQL,
    SqlParameter[]? parameters = null,
    SqlTransaction? transaction = null,
    CancellationToken cancellationToken = default);

// INSERT / UPDATE / DELETE — contra una conexión alternativa (sin transacción)
Task<Return_SQL_Action> NonQueryDataToSQLServer(
    string strNonQuerySQL,
    ConectionDescription ID_Connection,
    SqlParameter[]? parameters = null,
    CancellationToken cancellationToken = default);
```

### `Return_SQL_Action` — objeto de respuesta estándar

```csharp
public class Return_SQL_Action
{
    public bool Success { get; set; }           // true si no hubo excepción
    public string Message { get; set; }         // "Success" o mensaje de error
    public int RecordsAffected { get; set; }    // filas afectadas / leídas
    public DataTable? DTResults { get; set; }   // resultado de SELECT (null en NonQuery)
}
```

### `ConectionDescription` — conexión a base de datos alternativa

```csharp
var conexion = new ConectionDescription
{
    Server   = "OTRO_SERVIDOR",
    Database = "OTRA_BD",
    UserId   = "usuario",
    Password = "password",
    ConnectionTimeout = 60   // opcional, default 60s
};
```

### Registro en DI — `ServiceCollectionExtensions`

```csharp
// En Program.cs o Startup:
services.AddQualityGatesData();
// Registra automáticamente:
//   IDbConnectionFactory → SqlConnectionFactory (Singleton)
//   Class_Projectos (Scoped)
```

> **Regla crítica**: inyectar siempre `IDbConnectionFactory` (la interfaz), nunca `SqlConnectionFactory` directamente.
> Si se inyecta el concreto, el contenedor DI no lo puede resolver y falla en runtime.

## Patrón de Clases Parciales (Business Layer)

`Class_Projectos` usa `partial class` para organizar el código por áreas en ficheros separados.

**Reglas:**
- Todos los archivos parciales deben tener exactamente el mismo namespace: `QUALITY_GATES.Classes`
- El constructor y el campo `_db` van en `Quality_Gates.cs`
- Cada área funcional tiene su propio archivo: `Class_Projectos.NombreArea.cs`
- Los modelos van en `Models.cs`

**Para añadir una nueva área:**
```csharp
// Classes/Class_Projectos.Gates.cs
namespace QUALITY_GATES.Classes;

public partial class Class_Projectos
{
    public async Task<List<PROJECT_GATES>> GetGates(...) { ... }
}
```

## Patrones de Uso

### SELECT básico
```csharp
var queryResult = await _db.GetDatatableFromSelectAsync(sql, null, cancellationToken);
if (!queryResult.Success || queryResult.DTResults == null) return new List<X>();
foreach (DataRow row in queryResult.DTResults.Rows) { ... }
```

### NonQuery simple
```csharp
var result = await _db.NonQueryDataToSQLServer(
    "UPDATE TRA_PROJECTS SET ISGENERATED = 1 WHERE OPPORTUNITY_ID = @id",
    new[] { new SqlParameter("@id", opportunityId) }
);
```

### NonQuery con transacción (varias operaciones atómicas)
```csharp
await using var conn = await _db.CreateOpenConnectionAsync();
await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
try
{
    await _db.NonQueryDataToSQLServer(sql1, params1, transaction: tx);
    await _db.NonQueryDataToSQLServer(sql2, params2, transaction: tx);
    tx.Commit();
}
catch { tx.Rollback(); throw; }
```

## Convenciones de Código

- Usar `async/await` en todas las operaciones de base de datos
- `CancellationToken` como último parámetro opcional en todos los métodos async, con `default` como valor
- `await using` para gestionar `SqlConnection`, `SqlCommand`, `SqlDataReader`
- Nunca hardcodear connection strings — siempre desde `appsettings.json`
- Usar **siempre parámetros** `@param` en queries, nunca concatenar strings SQL
- `SqlTransaction` es un reference type — no necesita `ref` para ser usado en métodos

## Base de Datos

- **Servidor**: `CPDADMFA1` — SQL Server 2019 (instancia por defecto)
- **Base de datos**: `SRM`
- **Autenticación**: SQL Auth — credenciales en `appsettings.local.json`, nunca en código
- **Schema completo**: `C:\Programs\Quality Gates\Docs\Quality Gates.sql`
- **Naming convention**: tablas en `PREFIJO_NOMBRE_TABLA`, columnas en `UPPER_SNAKE_CASE`

### Tablas MAS_ (catálogos / master — no cambian por proyecto)

| Tabla | Propósito |
|---|---|
| `MAS_MODULES` | Módulo raíz (familia de producto) |
| `MAS_STATUS` | Fases por módulo: Feasibility, Design, Industrialización, SOP |
| `MAS_SUB_MODULES` | Definición de gates por fase — PK: `(MODULE_ID, STATUS_ID, SGATE_ID)` |
| `MAS_DELIVERABLES` | Entregables master por gate — PK: `(MODULE_ID, STATUS_ID, SGATE_ID, SEQUENCE)` |
| `MAS_DELIVERABLE_TYPE` | Tipo: `IS_FILE` y/o `IS_TEXT` |
| `MAS_DELIVERABLE_STATUS` | Estados: PENDING, INPROGRES, SUBMITTED, INREVIEW, APPROVED, REJECTED, ONHOLD, WAIVED |
| `MAS_GATE_STATUS` | Estados: NOTSTARTED, INPROGRES, COMPLETED, BLOCKED, WAIVED |
| `MAS_JOB_TITLES` | Funciones/roles RACI |
| `MAS_DEPARTMENTS` | Catálogo de departamentos |
| `MAS_FINISH_STATUS` | Estados de finalización |
| `MAS_USERS_CADENA` | Datos HR de usuarios (2306 registros): email, manager, jobTitle, JobRole — PK: `id` |

### Tablas TRA_ (transaccionales — una instancia por proyecto)

| Tabla | Propósito |
|---|---|
| `TRA_PROJECTS` | Proyectos importados de Salesforce — PK: `(OPPORTUNITY_ID, OPPORTUNITY_LINE_ID)` |
| `TRA_PROJECT_GATES` | **Eslabón clave**: instancia de cada gate por proyecto — PK surrogate: `TRA_GATE_ID` |
| `TRA_DELIVERABLES` | Instancia de cada entregable por proyecto-gate — referencia `TRA_GATE_ID` |
| `TRA_DELIVERABLE_FILES` | Ficheros subidos por entregable (soporta versiones) |
| `TRA_DELIVERABLE_APPROVALS` | Registro de aprobaciones/rechazos por revisor |
| `TRA_DELIVERABLE_COMMENTS` | Historial inmutable de comentarios (usuario + timestamp) |
| `TRA_GATES` | Tabla legacy — ignorar, reemplazada por `TRA_PROJECT_GATES` |

### Jerarquía de FKs — cadena completa

```
MAS_MODULES → MAS_STATUS → MAS_SUB_MODULES → MAS_DELIVERABLES
                                 ↑                    ↑
TRA_PROJECTS → TRA_PROJECT_GATES → TRA_DELIVERABLES ──┘
                       ↓                  ↓
               MAS_GATE_STATUS    MAS_DELIVERABLE_STATUS
               MAS_USERS_CADENA   (+ FILES, APPROVALS, COMMENTS)
```

> **Regla crítica**: nunca saltar `TRA_PROJECT_GATES` al insertar entregables transaccionales.

### Job Titles por fase (MAS_JOB_TITLES)

| Fase | Función líder | `JOB_TITLE_ID` en BD |
|---|---|---|
| Feasibility | Business Unit Director | `BUD` |
| Design | R&D Director | `RND_DIR` |
| Industrialización | Plant Manager | `PLANT_MGR` |
| Start of Production | Project Manager | `PM` |

### Notas sobre el schema
- `MAS_USERS_CADENA` tiene columnas en camelCase (inconsistente): `id`, `Email`, `jobTitle`, `JobRole`
- Typos en BD que NO se corrigen: `DELIVERABLE_ACEPTANCE_CRITERIA`, `SUPORTING_JOB_TITLE`, `DEPARMENT_DESCRIP`
- Al crear modelos C#, usar nombres correctos en propiedades pero mapear a los nombres reales en las queries SQL

## Problema NuGet Corporativo

La fuente corporativa `\\cpdfs01\itgrupopremo$\Developer\nuget` no siempre es accesible.
Existe `C:\Programs\Quality Gates\nuget.config` que la excluye para este proyecto.

**Síntoma**: errores `NU1301` en Visual Studio al hacer Rebuild.
**Causa**: el restore de NuGet intenta acceder a la ruta de red y falla.
**Solución aplicada**: `nuget.config` local en la raíz del proyecto con `<clear />` + solo `nuget.org` y `C:\NUGET`.
**Si recuperas acceso a la red corporativa**: elimina `nuget.config` de la raíz.

## Comandos Frecuentes

```bash
# Restaurar paquetes (ignorando fuentes de red no disponibles)
dotnet restore --ignore-failed-sources

# Compilar
dotnet build --no-restore

# Restaurar + compilar en un solo paso (forma recomendada en esta máquina)
dotnet restore "QUALITY_GATES\QUALITY_GATES.sln" --ignore-failed-sources && dotnet build "QUALITY_GATES\QUALITY_GATES.sln" --no-restore

# Limpiar artefactos (bin + obj)
dotnet clean

# Ejecutar tests
dotnet test
```

## Paquetes NuGet Clave

```xml
<PackageReference Include="Microsoft.Data.SqlClient" Version="5.*" />
<PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.*" />
<PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" Version="8.*" />
<PackageReference Include="MudBlazor" Version="7.*" />
```

## Connection String

En `appsettings.json`:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=TI_DATABASE;Server=CPDADMFA1;Initial Catalog=SRM;User Id=<usuario>;Password=<password>;Connection Timeout=60;TrustServerCertificate=True"
  }
}
```
> Las credenciales reales van en `appsettings.local.json` (excluido de git) o variables de entorno.

## Lo que NO hacer

- NO usar Entity Framework ni Dapper — solo `Microsoft.Data.SqlClient` directo
- NO mezclar lógica SQL en componentes Blazor (`.razor`) — va en las clases de `Classes\`
- NO inyectar `SqlConnectionFactory` directamente — siempre `IDbConnectionFactory`
- NO poner lógica de negocio y UI en el mismo fichero
- NO usar `Thread.Sleep` — siempre `await Task.Delay`
- NO concatenar strings SQL — siempre `SqlParameter`
- NO usar `ref` con tipos por referencia como `SqlTransaction` — es innecesario

## MudBlazor (cuando se implemente la UI)

- Usar siempre componentes MudBlazor en lugar de HTML plano
- Layout base: `MudLayout` + `MudAppBar` + `MudDrawer` + `MudMainContent`
- Tablas: `MudDataGrid` (preferido sobre `MudTable`)
- Formularios: `MudForm` con `DataAnnotations` o `FluentValidation`
- Notificaciones: `ISnackbar` inyectado, nunca `alert()`
- Diálogos: `IDialogService` con `MudDialog`

## Documentación de referencia del proyecto
- Detalle de acciones y entregables: `C:\Programs\Quality Gates\Docs\gates excel process de GL-00-001-F-001.xlsx`
- MudBlazor: https://mudblazor.com/docs/
- Microsoft.Data.SqlClient: https://learn.microsoft.com/en-us/sql/connect/ado-net/
- Blazor: https://learn.microsoft.com/en-us/aspnet/core/blazor/

## Estado del Proyecto

- [x] Schema de base de datos diseñado y aplicado en `CPDADMFA1\SRM`
- [x] Capa de datos implementada: `IDbConnectionFactory`, `SqlConnectionFactory`, `ConectionDescription`
- [x] `Class_Projectos` implementada con patrón partial class — `GetProjects()` funcionando
- [x] Registro DI configurado en `AddQualityGatesData()`
- [x] Problema NuGet corporativo resuelto con `nuget.config` local
- [x] App Tester WinForms operativa y compilando sin errores
- [ ] Layout principal con MudBlazor implementado
- [ ] Gates: listado de gates por proyecto
- [ ] Deliverables: gestión de entregables por gate
- [ ] Autenticación implementada
