using System.IO.Compression;
using System.Text;
using BODA.VMS.Web.Components;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Endpoints;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Windows Service 지원 (콘솔 실행 시에는 영향 없음)
builder.Host.UseWindowsService();

// Response Compression (WASM DLL 전송 크기 대폭 감소)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/octet-stream",
        "application/wasm",
        "application/javascript",
        "text/css"
    });
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    options.Level = CompressionLevel.Fastest);

// MudBlazor
builder.Services.AddMudServices();

// Razor + WASM
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// SQLite + EF Core (AuditInterceptor 적용)
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditInterceptor>();
builder.Services.AddDbContext<BodaVmsDbContext>((sp, options) =>
{
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"));
    options.AddInterceptors(sp.GetRequiredService<AuditInterceptor>());
});

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]!;
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        // Allow SignalR to receive token from query string
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

// SignalR
builder.Services.AddSignalR();

// Services
builder.Services.AddSingleton<JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IUserManagementService, UserManagementService>();
builder.Services.AddHttpClient();
builder.Services.Configure<VisionServerOptions>(
    builder.Configuration.GetSection(VisionServerOptions.SectionName));
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IRecipeParameterService, RecipeParameterService>();
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddScoped<IWorkOrderService, WorkOrderService>();
builder.Services.AddScoped<ILotService, LotService>();
builder.Services.AddScoped<IDefectCodeService, DefectCodeService>();
builder.Services.AddScoped<ISpcService, SpcService>();
builder.Services.AddScoped<IEquipmentStatusService, EquipmentStatusService>();
builder.Services.AddScoped<IOeeService, OeeService>();
builder.Services.AddScoped<IAlarmService, AlarmService>();
builder.Services.AddScoped<IAndonService, AndonService>();
builder.Services.AddScoped<IShiftService, ShiftService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IReportService, ReportService>();
builder.Services.AddScoped<IOperatorService, OperatorService>();
builder.Services.AddScoped<IOperatorSessionService, OperatorSessionService>();
builder.Services.AddScoped<IMaintenanceService, MaintenanceService>();
builder.Services.AddScoped<IReliabilityService, ReliabilityService>();
builder.Services.AddHostedService<ClientMonitorService>();

var app = builder.Build();

// Shared DB: VisionServer owns the schema. Web adds only its own columns/tables.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();

    // 1. Add Web-specific columns to VisionServer's Clients table
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "PRAGMA table_info(Clients);";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("IsActive"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN IsActive INTEGER NOT NULL DEFAULT 1;");
        if (!columns.Contains("CreatedAt"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '2025-01-01T00:00:00';");
        if (!columns.Contains("LastSeenAt"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN LastSeenAt TEXT;");
        if (!columns.Contains("LastHeartbeatIp"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN LastHeartbeatIp TEXT;");
        if (!columns.Contains("HostName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN HostName TEXT;");
        if (!columns.Contains("SwName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN SwName TEXT;");
    }

    // 1-b. VisionServer가 만든 기존 레코드에 IsActive가 0인 경우 1로 보정
    //      (VisionServer는 IsActive 컬럼을 모르므로 INSERT 시 DEFAULT에 의존하지만,
    //       EF6가 UPDATE할 때 0으로 덮어쓸 가능성에 대비)
    await db.Database.ExecuteSqlRawAsync(
        "UPDATE Clients SET IsActive = 1 WHERE IsActive = 0 AND LastSeenAt IS NULL;");

    // 2. Create Web-only tables (Users, InspectionHistories)
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""Users"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""Username""    TEXT NOT NULL,
            ""PasswordHash"" TEXT NOT NULL,
            ""DisplayName"" TEXT NOT NULL,
            ""Role""        TEXT NOT NULL DEFAULT 'Pending',
            ""IsApproved""  INTEGER NOT NULL DEFAULT 0,
            ""CreatedAt""   TEXT NOT NULL,
            ""ApprovedAt""  TEXT
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Users_Username\" ON \"Users\" (\"Username\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""InspectionHistories"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""ClientId""    INTEGER NOT NULL,
            ""RecipeName""  TEXT,
            ""IsPass""      INTEGER NOT NULL DEFAULT 0,
            ""NgCode""      TEXT,
            ""ToolResults"" TEXT,
            ""ImagePath""   TEXT,
            ""InspectedAt"" TEXT NOT NULL,
            FOREIGN KEY (""ClientId"") REFERENCES ""Clients"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_InspectionHistories_InspectedAt\" ON \"InspectionHistories\" (\"InspectedAt\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_InspectionHistories_ClientId_InspectedAt\" ON \"InspectionHistories\" (\"ClientId\", \"InspectedAt\");");

    // 2-b. Add Web-specific columns to VisionServer's Recipes table
    using (var cmdRecipe = conn.CreateCommand())
    {
        cmdRecipe.CommandText = "PRAGMA table_info(Recipes);";
        var recipeColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var readerRecipe = await cmdRecipe.ExecuteReaderAsync())
        {
            while (await readerRecipe.ReadAsync())
                recipeColumns.Add(readerRecipe.GetString(1));
        }

        if (!recipeColumns.Contains("Description"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Recipes ADD COLUMN Description TEXT;");
        if (!recipeColumns.Contains("CreatedAt"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Recipes ADD COLUMN CreatedAt TEXT;");
    }

    // 2-c. Create/Migrate RecipeParameters table (레시피-파라미터 계층 구조)
    // FK가 잘못된 기존 테이블이 있으면 재생성
    using (var cmdFk = conn.CreateCommand())
    {
        bool needsRecreate = false;
        cmdFk.CommandText = "PRAGMA foreign_key_list(RecipeParameters);";
        try
        {
            using (var readerFk = await cmdFk.ExecuteReaderAsync())
            {
                if (await readerFk.ReadAsync())
                {
                    // to 컬럼(index 4)이 "RecipeID"가 아니면 FK가 잘못된 것
                    var toColumn = readerFk.GetString(4);
                    if (toColumn != "RecipeID")
                        needsRecreate = true;
                }
            }
        }
        catch
        {
            // 테이블이 없으면 새로 생성
        }

        if (needsRecreate)
        {
            await db.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"RecipeParameters\";");
        }
    }

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""RecipeParameters"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""RecipeId""    INTEGER NOT NULL,
            ""ParamCode""   INTEGER NOT NULL,
            ""ParamValue""  REAL NOT NULL DEFAULT 0,
            ""Description"" TEXT NOT NULL,
            ""Category""    TEXT NOT NULL DEFAULT 'Dimension',
            ""Unit""        TEXT,
            ""IsActive""    INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt""   TEXT NOT NULL,
            ""UpdatedAt""   TEXT,
            FOREIGN KEY (""RecipeId"") REFERENCES ""Recipes"" (""RecipeID"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_RecipeParameters_RecipeId_ParamCode\" ON \"RecipeParameters\" (\"RecipeId\", \"ParamCode\");");

    // 2-d. MES Phase 1: Products, WorkOrders, Lots 테이블 생성 + InspectionHistories 추적성 컬럼
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""Products"" (
            ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""Code""            TEXT NOT NULL,
            ""Name""            TEXT NOT NULL,
            ""Specification""   TEXT,
            ""DefaultRecipeId"" INTEGER,
            ""IsActive""        INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt""       TEXT NOT NULL,
            ""UpdatedAt""       TEXT,
            FOREIGN KEY (""DefaultRecipeId"") REFERENCES ""Recipes"" (""RecipeID"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Products_Code\" ON \"Products\" (\"Code\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""WorkOrders"" (
            ""Id""                INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""OrderNo""           TEXT NOT NULL,
            ""ProductId""         INTEGER NOT NULL,
            ""ClientId""          INTEGER NOT NULL,
            ""RecipeId""          INTEGER NOT NULL,
            ""PlannedQuantity""   INTEGER NOT NULL DEFAULT 0,
            ""ProducedQuantity""  INTEGER NOT NULL DEFAULT 0,
            ""PassQuantity""      INTEGER NOT NULL DEFAULT 0,
            ""NgQuantity""        INTEGER NOT NULL DEFAULT 0,
            ""Status""            TEXT NOT NULL DEFAULT 'Planned',
            ""PlannedStartAt""    TEXT,
            ""ActualStartAt""     TEXT,
            ""ActualEndAt""       TEXT,
            ""Note""              TEXT,
            ""CreatedAt""         TEXT NOT NULL,
            ""UpdatedAt""         TEXT,
            FOREIGN KEY (""ProductId"") REFERENCES ""Products"" (""Id""),
            FOREIGN KEY (""ClientId"")  REFERENCES ""Clients"" (""Id""),
            FOREIGN KEY (""RecipeId"")  REFERENCES ""Recipes"" (""RecipeID"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_WorkOrders_OrderNo\" ON \"WorkOrders\" (\"OrderNo\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_WorkOrders_Status_PlannedStartAt\" ON \"WorkOrders\" (\"Status\", \"PlannedStartAt\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""Lots"" (
            ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""LotNumber""    TEXT NOT NULL,
            ""WorkOrderId""  INTEGER NOT NULL,
            ""Sequence""     INTEGER NOT NULL,
            ""Quantity""     INTEGER NOT NULL DEFAULT 0,
            ""PassCount""    INTEGER NOT NULL DEFAULT 0,
            ""NgCount""      INTEGER NOT NULL DEFAULT 0,
            ""Status""       TEXT NOT NULL DEFAULT 'Open',
            ""CreatedAt""    TEXT NOT NULL,
            ""ClosedAt""     TEXT,
            ""Note""         TEXT,
            FOREIGN KEY (""WorkOrderId"") REFERENCES ""WorkOrders"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Lots_LotNumber\" ON \"Lots\" (\"LotNumber\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Lots_WorkOrderId_Sequence\" ON \"Lots\" (\"WorkOrderId\", \"Sequence\");");

    // InspectionHistories에 추적성 컬럼 추가
    using (var cmdHist = conn.CreateCommand())
    {
        cmdHist.CommandText = "PRAGMA table_info(InspectionHistories);";
        var histColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var readerHist = await cmdHist.ExecuteReaderAsync())
        {
            while (await readerHist.ReadAsync())
                histColumns.Add(readerHist.GetString(1));
        }

        if (!histColumns.Contains("WorkOrderId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE InspectionHistories ADD COLUMN WorkOrderId INTEGER;");
        if (!histColumns.Contains("LotId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE InspectionHistories ADD COLUMN LotId INTEGER;");
        if (!histColumns.Contains("OperatorId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE InspectionHistories ADD COLUMN OperatorId INTEGER;");
        if (!histColumns.Contains("SerialNumber"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE InspectionHistories ADD COLUMN SerialNumber TEXT;");
    }
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_InspectionHistories_WorkOrderId\" ON \"InspectionHistories\" (\"WorkOrderId\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_InspectionHistories_LotId\" ON \"InspectionHistories\" (\"LotId\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_InspectionHistories_SerialNumber\" ON \"InspectionHistories\" (\"SerialNumber\");");

    // 2-e. MES Phase 2: DefectCodes, ParameterMeasurements 테이블 + RecipeParameters USL/LSL 컬럼
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""DefectCodes"" (
            ""Id""           INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""Code""         TEXT NOT NULL,
            ""Description""  TEXT NOT NULL,
            ""Category""     TEXT,
            ""Severity""     TEXT NOT NULL DEFAULT 'Major',
            ""IsActive""     INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt""    TEXT NOT NULL,
            ""UpdatedAt""    TEXT
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_DefectCodes_Code\" ON \"DefectCodes\" (\"Code\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""ParameterMeasurements"" (
            ""Id""             INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""HistoryId""      INTEGER NOT NULL,
            ""RecipeId""       INTEGER NOT NULL,
            ""ParamCode""      INTEGER NOT NULL,
            ""MeasuredValue""  REAL NOT NULL,
            ""Judgment""       TEXT NOT NULL DEFAULT 'OK',
            ""InspectedAt""    TEXT NOT NULL,
            ""ClientId""       INTEGER NOT NULL,
            ""WorkOrderId""    INTEGER,
            ""LotId""          INTEGER,
            FOREIGN KEY (""HistoryId"") REFERENCES ""InspectionHistories"" (""Id"") ON DELETE CASCADE
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_ParameterMeasurements_RecipeId_ParamCode_InspectedAt\" ON \"ParameterMeasurements\" (\"RecipeId\", \"ParamCode\", \"InspectedAt\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_ParameterMeasurements_HistoryId\" ON \"ParameterMeasurements\" (\"HistoryId\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_ParameterMeasurements_WorkOrderId\" ON \"ParameterMeasurements\" (\"WorkOrderId\");");

    // RecipeParameters에 USL/LSL 컬럼 추가
    using (var cmdRp = conn.CreateCommand())
    {
        cmdRp.CommandText = "PRAGMA table_info(RecipeParameters);";
        var rpColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var readerRp = await cmdRp.ExecuteReaderAsync())
        {
            while (await readerRp.ReadAsync())
                rpColumns.Add(readerRp.GetString(1));
        }

        if (!rpColumns.Contains("LowerLimit"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE RecipeParameters ADD COLUMN LowerLimit REAL;");
        if (!rpColumns.Contains("UpperLimit"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE RecipeParameters ADD COLUMN UpperLimit REAL;");
    }

    // 2-f. MES Phase 2 (OEE + Alarms): EquipmentStatusLogs, AlarmEvents 테이블
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""EquipmentStatusLogs"" (
            ""Id""         INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""ClientId""   INTEGER NOT NULL,
            ""Status""     TEXT NOT NULL DEFAULT 'Idle',
            ""StartedAt""  TEXT NOT NULL,
            ""EndedAt""    TEXT,
            ""Reason""     TEXT,
            FOREIGN KEY (""ClientId"") REFERENCES ""Clients"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_EquipmentStatusLogs_ClientId_StartedAt\" ON \"EquipmentStatusLogs\" (\"ClientId\", \"StartedAt\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_EquipmentStatusLogs_ClientId_EndedAt\" ON \"EquipmentStatusLogs\" (\"ClientId\", \"EndedAt\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""AlarmEvents"" (
            ""Id""                  INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""ClientId""            INTEGER,
            ""AlarmType""           TEXT NOT NULL DEFAULT 'NG',
            ""Severity""            TEXT NOT NULL DEFAULT 'Major',
            ""Title""               TEXT NOT NULL,
            ""Message""             TEXT,
            ""OccurredAt""          TEXT NOT NULL,
            ""AcknowledgedAt""      TEXT,
            ""AcknowledgedBy""      INTEGER,
            ""AcknowledgedByName""  TEXT,
            ""ResolvedAt""          TEXT,
            ""ResolvedBy""          INTEGER,
            ""ResolvedByName""      TEXT,
            ""Resolution""          TEXT,
            ""RelatedHistoryId""    INTEGER,
            FOREIGN KEY (""ClientId"") REFERENCES ""Clients"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_AlarmEvents_OccurredAt\" ON \"AlarmEvents\" (\"OccurredAt\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_AlarmEvents_ClientId_OccurredAt\" ON \"AlarmEvents\" (\"ClientId\", \"OccurredAt\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_AlarmEvents_State\" ON \"AlarmEvents\" (\"AcknowledgedAt\", \"ResolvedAt\");");

    // 2-g. MES Phase 3 (AuditLog + Shift): Shifts, AuditLogs 테이블 + InspectionHistories.ShiftId 컬럼
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""Shifts"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""Name""        TEXT NOT NULL,
            ""StartHour""   INTEGER NOT NULL,
            ""EndHour""     INTEGER NOT NULL,
            ""IsActive""    INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt""   TEXT NOT NULL
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Shifts_Name\" ON \"Shifts\" (\"Name\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""AuditLogs"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""EntityName""  TEXT NOT NULL,
            ""EntityId""    TEXT,
            ""Action""      TEXT NOT NULL,
            ""Changes""     TEXT,
            ""Timestamp""   TEXT NOT NULL,
            ""UserId""      INTEGER,
            ""UserName""    TEXT,
            ""IpAddress""   TEXT
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_Timestamp\" ON \"AuditLogs\" (\"Timestamp\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_EntityName_EntityId\" ON \"AuditLogs\" (\"EntityName\", \"EntityId\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_AuditLogs_UserId\" ON \"AuditLogs\" (\"UserId\");");

    // InspectionHistories.ShiftId 컬럼 추가
    using (var cmdSh = conn.CreateCommand())
    {
        cmdSh.CommandText = "PRAGMA table_info(InspectionHistories);";
        var hCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = await cmdSh.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                hCols.Add(reader.GetString(1));
        }
        if (!hCols.Contains("ShiftId"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE InspectionHistories ADD COLUMN ShiftId INTEGER;");
    }
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_InspectionHistories_ShiftId\" ON \"InspectionHistories\" (\"ShiftId\");");

    // 2-h. MES Phase 3-잔여 (Operator Logging): Operators, OperatorSessions 테이블
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""Operators"" (
            ""Id""              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""EmployeeNumber""  TEXT NOT NULL,
            ""Name""            TEXT NOT NULL,
            ""PinHash""         TEXT NOT NULL,
            ""Department""      TEXT,
            ""IsActive""        INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt""       TEXT NOT NULL,
            ""UpdatedAt""       TEXT
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_Operators_EmployeeNumber\" ON \"Operators\" (\"EmployeeNumber\");");

    // D10: Operators 테이블에 Role 컬럼 추가 (기존 DB 마이그레이션)
    using (var cmdOp = conn.CreateCommand())
    {
        cmdOp.CommandText = "PRAGMA table_info(Operators);";
        var operatorColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var readerOp = await cmdOp.ExecuteReaderAsync())
        {
            while (await readerOp.ReadAsync())
                operatorColumns.Add(readerOp.GetString(1));
        }

        if (!operatorColumns.Contains("Role"))
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Operators ADD COLUMN Role TEXT NOT NULL DEFAULT 'Operator';");
    }

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""OperatorSessions"" (
            ""Id""          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""OperatorId""  INTEGER NOT NULL,
            ""ClientId""    INTEGER NOT NULL,
            ""StartedAt""   TEXT NOT NULL,
            ""EndedAt""     TEXT,
            ""EndReason""   TEXT,
            FOREIGN KEY (""OperatorId"") REFERENCES ""Operators"" (""Id""),
            FOREIGN KEY (""ClientId"") REFERENCES ""Clients"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_OperatorSessions_ClientId_EndedAt\" ON \"OperatorSessions\" (\"ClientId\", \"EndedAt\");");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_OperatorSessions_OperatorId_StartedAt\" ON \"OperatorSessions\" (\"OperatorId\", \"StartedAt\");");

    // 2-i. G16 (Maintenance / PM): MaintenanceSchedules, MaintenanceRecords 테이블
    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""MaintenanceSchedules"" (
            ""Id""                          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""ClientId""                    INTEGER,
            ""Name""                        TEXT NOT NULL,
            ""Description""                 TEXT,
            ""IntervalDays""                INTEGER NOT NULL DEFAULT 7,
            ""EstimatedDurationMinutes""    INTEGER NOT NULL DEFAULT 30,
            ""LastPerformedAt""             TEXT,
            ""NextDueAt""                   TEXT NOT NULL,
            ""IsActive""                    INTEGER NOT NULL DEFAULT 1,
            ""CreatedAt""                   TEXT NOT NULL,
            ""UpdatedAt""                   TEXT,
            FOREIGN KEY (""ClientId"") REFERENCES ""Clients"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_MaintenanceSchedules_Active_Due\" ON \"MaintenanceSchedules\" (\"IsActive\", \"NextDueAt\");");

    await db.Database.ExecuteSqlRawAsync(@"
        CREATE TABLE IF NOT EXISTS ""MaintenanceRecords"" (
            ""Id""                      INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
            ""ScheduleId""              INTEGER NOT NULL,
            ""ClientId""                INTEGER,
            ""PerformedAt""             TEXT NOT NULL,
            ""ActualDurationMinutes""   INTEGER,
            ""PerformedByUserId""       INTEGER,
            ""PerformedByName""         TEXT,
            ""Notes""                   TEXT,
            ""PreviousDueAt""           TEXT NOT NULL,
            ""NewDueAt""                TEXT NOT NULL,
            FOREIGN KEY (""ScheduleId"") REFERENCES ""MaintenanceSchedules"" (""Id""),
            FOREIGN KEY (""ClientId"") REFERENCES ""Clients"" (""Id"")
        );");
    await db.Database.ExecuteSqlRawAsync(
        "CREATE INDEX IF NOT EXISTS \"IX_MaintenanceRecords_Schedule_Performed\" ON \"MaintenanceRecords\" (\"ScheduleId\", \"PerformedAt\");");

    // Default Shifts 시드 (한국 표준 3교대) — 빈 테이블일 때만
    using (var cmdSeed = conn.CreateCommand())
    {
        cmdSeed.CommandText = "SELECT COUNT(*) FROM Shifts;";
        var shiftCount = Convert.ToInt64(await cmdSeed.ExecuteScalarAsync());
        if (shiftCount == 0)
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Shifts (Name, StartHour, EndHour, IsActive, CreatedAt) VALUES " +
                "('1조 (주간)', 8, 16, 1, {0})," +
                "('2조 (저녁)', 16, 0, 1, {0})," +
                "('3조 (야간)', 0, 8, 1, {0});",
                now);
        }
    }

    // 3. Seed default admin user if not exists
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Username = 'admin';";
        var adminExists = Convert.ToInt64(await cmd.ExecuteScalarAsync()) > 0;
        if (!adminExists)
        {
            var hash = BCrypt.Net.BCrypt.HashPassword("admin");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Users (Username, PasswordHash, DisplayName, Role, IsApproved, CreatedAt) VALUES ('admin', {0}, 'Administrator', 'Admin', 1, '2025-01-01T00:00:00');",
                hash);
        }
    }

    // 4. Apply SQLite pragmas
    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;");
    await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
}

// Configure the HTTP request pipeline.
app.UseResponseCompression();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
    app.UseHttpsRedirection();
}
// Static files with aggressive caching for WASM framework files
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var name = ctx.File.Name;
        var path = ctx.Context.Request.Path.Value ?? "";

        // _framework/ DLLs/WASM/ICU change only on deploy → cache aggressively
        // (압축된 .br/.gz 변형까지 포함하도록 Contains 사용)
        if (path.Contains("/_framework/") ||
            name.Contains(".dll") || name.Contains(".wasm") ||
            name.Contains(".dat") || name.Contains(".blat") ||
            name.Contains(".pdb"))
        {
            // blazor.boot.json은 매 배포마다 바뀌므로 revalidate
            if (name.Contains("blazor.boot.json"))
                ctx.Context.Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
            else
                ctx.Context.Response.Headers.CacheControl = "public, max-age=604800, immutable";
        }
        // CSS/JS with versioning
        else if (name.EndsWith(".css") || name.EndsWith(".js"))
        {
            ctx.Context.Response.Headers.CacheControl = "public, max-age=86400";
        }
        // i18n JSON — 항상 최신 (개발 중 키 추가/변경 시 즉시 반영)
        else if (path.StartsWith("/i18n/") && name.EndsWith(".json"))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            ctx.Context.Response.Headers.Pragma = "no-cache";
        }
    }
});
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// Map API endpoints
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapClientEndpoints();
app.MapInspectionItemEndpoints();
app.MapHistoryEndpoints();
app.MapSettingsEndpoints();
app.MapProductEndpoints();
app.MapWorkOrderEndpoints();
app.MapLotEndpoints();
app.MapDefectCodeEndpoints();
app.MapSpcEndpoints();
app.MapOeeEndpoints();
app.MapAlarmEndpoints();
app.MapAndonEndpoints();
app.MapShiftEndpoints();
app.MapAuditLogEndpoints();
app.MapReportEndpoints();
app.MapOperatorEndpoints();
app.MapMaintenanceEndpoints();

// Map SignalR hub
app.MapHub<VmsHub>("/hubs/vms");
app.MapHub<VmsPublicHub>("/hubs/vms-public"); // C5: 익명 — VMS 클라이언트 운영 흐름 푸시용

// Razor Component endpoint는 익명 허용 — 페이지의 [Authorize]는 클라이언트 측 AuthorizeRouteView가 처리.
// (JWT를 localStorage에 저장하므로 F5 시 서버로 토큰이 전달되지 않아 401이 발생하지 않도록 함)
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BODA.VMS.Web.Client._Imports).Assembly)
    .AllowAnonymous();

app.Run();
