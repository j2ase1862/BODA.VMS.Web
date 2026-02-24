using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Blazored.LocalStorage;
using Microsoft.AspNetCore.Components.Authorization;

namespace BODA.VMS.Web.Client.Services;

public class AuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = "authToken";
    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _http;

    public AuthStateProvider(ILocalStorageService localStorage, HttpClient http)
    {
        _localStorage = localStorage;
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _localStorage.GetItemAsStringAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        // Remove surrounding quotes if present
        token = token.Trim('"');

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            await _localStorage.RemoveItemAsync(TokenKey);
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var jwt = handler.ReadJwtToken(token);
        if (jwt.ValidTo < DateTime.UtcNow)
        {
            await _localStorage.RemoveItemAsync(TokenKey);
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task LoginAsync(string token)
    {
        await _localStorage.SetItemAsStringAsync(TokenKey, token);
        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task LogoutAsync()
    {
        await _localStorage.RemoveItemAsync(TokenKey);
        _http.DefaultRequestHeaders.Authorization = null;
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task<string?> GetTokenAsync()
    {
        var token = await _localStorage.GetItemAsStringAsync(TokenKey);
        return token?.Trim('"');
    }
}
