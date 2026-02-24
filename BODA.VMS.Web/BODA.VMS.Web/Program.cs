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
builder.Services.AddScoped<IClientService, ClientService>();
builder.Services.AddScoped<IHistoryService, HistoryService>();
builder.Services.AddHostedService<ClientMonitorService>();

var app = builder.Build();

// Ensure database is created and apply WAL mode
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BodaVmsDbContext>();
    await db.Database.EnsureCreatedAsync();

    // Add heartbeat metadata columns if missing (safe for existing DBs)
    var conn = db.Database.GetDbConnection();
    await conn.OpenAsync();
    using (var cmd = conn.CreateCommand())
    {
        cmd.CommandText = "PRAGMA table_info(Clients);";
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                columns.Add(reader.GetString(1));
        }

        if (!columns.Contains("LastHeartbeatIp"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN LastHeartbeatIp TEXT;");
        if (!columns.Contains("HostName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN HostName TEXT;");
        if (!columns.Contains("SwName"))
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Clients ADD COLUMN SwName TEXT;");
    }

    // Apply SQLite pragmas
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
app.MapHistoryEndpoints();

// Map SignalR hub
app.MapHub<VmsHub>("/hubs/vms");

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BODA.VMS.Web.Client._Imports).Assembly);

app.Run();
