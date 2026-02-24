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
}
