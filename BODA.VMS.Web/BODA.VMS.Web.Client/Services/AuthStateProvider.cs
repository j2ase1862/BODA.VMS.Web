using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using Blazored.LocalStorage;
using BODA.VMS.Web.Client.Models;
using Microsoft.AspNetCore.Components.Authorization;

namespace BODA.VMS.Web.Client.Services;

public class AuthStateProvider : AuthenticationStateProvider
{
    private const string TokenKey = "authToken";
    private const string RefreshKey = "refreshToken";
    private readonly ILocalStorageService _localStorage;
    private readonly HttpClient _http;

    // 만료 토큰에 대해 동시 다발 refresh 요청이 폭주하지 않도록 직렬화
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthStateProvider(ILocalStorageService localStorage, HttpClient http)
    {
        _localStorage = localStorage;
        _http = http;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _localStorage.GetItemAsStringAsync(TokenKey);
        if (string.IsNullOrWhiteSpace(token))
            return Anonymous();

        // Remove surrounding quotes if present
        token = token.Trim('"');

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            await ClearAsync();
            return Anonymous();
        }

        var jwt = handler.ReadJwtToken(token);
        if (jwt.ValidTo < DateTime.UtcNow)
        {
            // GS 보안 — access token 만료: refresh token 으로 재발급 시도 (재로그인 회피).
            token = await TryRefreshAsync();
            if (token is null)
            {
                await ClearAsync();
                return Anonymous();
            }
            jwt = handler.ReadJwtToken(token);
        }

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var identity = new ClaimsIdentity(jwt.Claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task LoginAsync(LoginResponse response)
    {
        await _localStorage.SetItemAsStringAsync(TokenKey, response.Token);
        if (!string.IsNullOrWhiteSpace(response.RefreshToken))
            await _localStorage.SetItemAsStringAsync(RefreshKey, response.RefreshToken);

        _http.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", response.Token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task LogoutAsync()
    {
        // 서버측 refresh token 폐기 (revocation) — 토큰이 남아 재사용되는 것 방지
        var refresh = (await _localStorage.GetItemAsStringAsync(RefreshKey))?.Trim('"');
        if (!string.IsNullOrWhiteSpace(refresh))
        {
            try { await _http.PostAsJsonAsync("/api/auth/logout", new RefreshRequest { RefreshToken = refresh }); }
            catch { /* 네트워크 실패해도 로컬 정리는 진행 */ }
        }

        await ClearAsync();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task<string?> GetTokenAsync()
    {
        var token = await _localStorage.GetItemAsStringAsync(TokenKey);
        return token?.Trim('"');
    }

    /// <summary>저장된 refresh token 으로 새 access token 발급. 실패 시 null.</summary>
    private async Task<string?> TryRefreshAsync()
    {
        var refresh = (await _localStorage.GetItemAsStringAsync(RefreshKey))?.Trim('"');
        if (string.IsNullOrWhiteSpace(refresh)) return null;

        await _refreshLock.WaitAsync();
        try
        {
            // 락 대기 중 다른 호출이 이미 갱신했을 수 있음 — 재확인
            var current = (await _localStorage.GetItemAsStringAsync(TokenKey))?.Trim('"');
            if (!string.IsNullOrWhiteSpace(current))
            {
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(current) && handler.ReadJwtToken(current).ValidTo >= DateTime.UtcNow)
                    return current;
            }

            var resp = await _http.PostAsJsonAsync("/api/auth/refresh",
                new RefreshRequest { RefreshToken = refresh });
            if (!resp.IsSuccessStatusCode) return null;

            var login = await resp.Content.ReadFromJsonAsync<LoginResponse>();
            if (login is null || string.IsNullOrWhiteSpace(login.Token)) return null;

            await _localStorage.SetItemAsStringAsync(TokenKey, login.Token);
            if (!string.IsNullOrWhiteSpace(login.RefreshToken))
                await _localStorage.SetItemAsStringAsync(RefreshKey, login.RefreshToken);
            return login.Token;
        }
        catch
        {
            return null;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task ClearAsync()
    {
        await _localStorage.RemoveItemAsync(TokenKey);
        await _localStorage.RemoveItemAsync(RefreshKey);
        _http.DefaultRequestHeaders.Authorization = null;
    }

    private static AuthenticationState Anonymous() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));
}
