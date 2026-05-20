using Blazored.LocalStorage;
using BODA.VMS.Web.Client.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddAuthorizationCore();

builder.Services.AddScoped(sp =>
    new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp =>
    sp.GetRequiredService<AuthStateProvider>());

builder.Services.AddScoped<ApiClient>();
builder.Services.AddScoped<SignalRService>();
builder.Services.AddScoped<ILocalizer, LocalizationService>();

var host = builder.Build();

// i18n 초기 로드 — 타임아웃으로 부팅 지연 방지 (실패해도 raw key로 동작)
var localizer = host.Services.GetRequiredService<ILocalizer>();
try
{
    await localizer.InitializeAsync().WaitAsync(TimeSpan.FromSeconds(3));
}
catch
{
    // 네트워크/스토리지 실패 무시
}

// SignalR은 백그라운드 시작
var signalR = host.Services.GetRequiredService<SignalRService>();
_ = signalR.StartAsync();

await host.RunAsync();
