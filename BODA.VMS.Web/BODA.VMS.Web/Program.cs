using System.Text;
using BODA.VMS.Web.Components;
using BODA.VMS.Web.Data;
using BODA.VMS.Web.Endpoints;
using BODA.VMS.Web.Hubs;
using BODA.VMS.Web.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Windows Service 지원 (콘솔 실행 시에는 영향 없음)
builder.Host.UseWindowsService();

// MudBlazor
builder.Services.AddMudServices();

// Razor + WASM
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

// SQLite + EF Core
builder.Services.AddDbContext<BodaVmsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

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
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IRecipeParameterService, RecipeParameterService>();
builder.Services.AddScoped<IHistoryService, HistoryService>();
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
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

// Map API endpoints
app.MapAuthEndpoints();
app.MapAdminEndpoints();
app.MapClientEndpoints();
app.MapInspectionItemEndpoints();
app.MapHistoryEndpoints();

// Map SignalR hub
app.MapHub<VmsHub>("/hubs/vms");

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BODA.VMS.Web.Client._Imports).Assembly);

app.Run();
