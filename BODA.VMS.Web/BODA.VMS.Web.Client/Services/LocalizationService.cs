using System.Net.Http.Json;
using System.Text.Json;
using Blazored.LocalStorage;

namespace BODA.VMS.Web.Client.Services;

public class LocalizationService : ILocalizer
{
    private readonly HttpClient _http;
    private readonly ILocalStorageService _storage;

    private const string StorageKey = "vms_language";
    private const string DefaultLanguage = "ko";

    private static readonly IReadOnlyList<string> _availableLanguages = new[] { "ko", "en" };

    private Dictionary<string, string> _resources = new();
    private string _currentLanguage = DefaultLanguage;

    public event Action? OnLanguageChanged;

    public string CurrentLanguage => _currentLanguage;
    public IReadOnlyList<string> AvailableLanguages => _availableLanguages;

    public LocalizationService(HttpClient http, ILocalStorageService storage)
    {
        _http = http;
        _storage = storage;
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return "";
            return _resources.TryGetValue(key, out var v) ? v : key;
        }
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        if (args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch
        {
            return template;
        }
    }

    public async Task InitializeAsync()
    {
        string lang = DefaultLanguage;
        try
        {
            var saved = await _storage.GetItemAsync<string>(StorageKey);
            if (!string.IsNullOrEmpty(saved) && _availableLanguages.Contains(saved))
                lang = saved;
        }
        catch
        {
            // LocalStorage 접근 실패 — 기본 언어 유지
        }
        await LoadAsync(lang);
        // 백그라운드 로드 완료 후 구독 컴포넌트들이 재렌더링되도록 알림
        OnLanguageChanged?.Invoke();
    }

    public async Task SetLanguageAsync(string language)
    {
        if (!_availableLanguages.Contains(language)) return;
        if (language == _currentLanguage) return;

        await LoadAsync(language);
        try
        {
            await _storage.SetItemAsync(StorageKey, language);
        }
        catch
        {
            // 저장 실패는 무시
        }
        OnLanguageChanged?.Invoke();
    }

    private async Task LoadAsync(string language)
    {
        try
        {
            // 캐시 버스팅 — 매번 새 요청으로 최신 번역 보장
            var bust = DateTime.UtcNow.Ticks;
            var json = await _http.GetFromJsonAsync<JsonElement>($"i18n/{language}.json?v={bust}");
            var flat = new Dictionary<string, string>(StringComparer.Ordinal);
            Flatten(json, "", flat);
            _resources = flat;
            _currentLanguage = language;
        }
        catch
        {
            // JSON 로드 실패 — 기존 resource 유지
        }
    }

    /// <summary>중첩 JSON을 점 구분 평면 키로 변환. {"menu":{"dashboard":"X"}} → {"menu.dashboard":"X"}</summary>
    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> dest)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in element.EnumerateObject())
            {
                var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                Flatten(prop.Value, key, dest);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            dest[prefix] = element.GetString() ?? "";
        }
        // 그 외 타입은 무시 (i18n에서는 문자열만 사용)
    }
}
