using System.Net.Http.Json;

namespace BODA.VMS.Web.Client.Services;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<T?> GetAsync<T>(string url)
    {
        var response = await _http.GetAsync(url);
        // 인증 실패는 일반적인 경우(미로그인 등)이므로 예외 대신 null 반환
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task PostAsync<TRequest>(string url, TRequest data)
    {
        var response = await _http.PostAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string url, TRequest data)
    {
        var response = await _http.PutAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task DeleteAsync(string url)
    {
        var response = await _http.DeleteAsync(url);
        response.EnsureSuccessStatusCode();
    }

    public async Task<HttpResponseMessage> GetResponseAsync(string url)
    {
        return await _http.GetAsync(url);
    }

    public async Task<byte[]> GetBytesAsync(string url)
    {
        return await _http.GetByteArrayAsync(url);
    }

    /// <summary>POST로 바이너리(PDF/Excel 등) 응답 받기. 응답 파일명 (Content-Disposition)도 같이 반환.</summary>
    public async Task<(byte[] Bytes, string FileName)> PostForFileAsync<TRequest>(string url, TRequest data, string fallbackName)
    {
        var response = await _http.PostAsJsonAsync(url, data);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
                    ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                    ?? fallbackName;
        return (bytes, fileName);
    }
}
