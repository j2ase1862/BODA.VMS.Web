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

var host = builder.Build();

// Start SignalR after host is built
var signalR = host.Services.GetRequiredService<SignalRService>();
_ = signalR.StartAsync();

await host.RunAsync();
