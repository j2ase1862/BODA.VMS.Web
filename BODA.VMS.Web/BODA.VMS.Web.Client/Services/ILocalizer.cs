namespace BODA.VMS.Web.Client.Services;

/// <summary>다국어 리소스 접근. 키가 없으면 키 자체를 반환합니다.</summary>
public interface ILocalizer
{
    /// <summary>현재 언어 코드 (ko, en).</summary>
    string CurrentLanguage { get; }

    /// <summary>사용 가능한 언어 코드 목록.</summary>
    IReadOnlyList<string> AvailableLanguages { get; }

    /// <summary>키로 번역값 조회. 키가 없으면 키 자체 반환. 점 구분(menu.dashboard) 지원.</summary>
    string this[string key] { get; }

    /// <summary>형식 문자열 ({0}, {1}) 치환.</summary>
    string Format(string key, params object[] args);

    /// <summary>언어 변경. JSON 로드 후 OnLanguageChanged 이벤트 발생.</summary>
    Task SetLanguageAsync(string language);

    /// <summary>초기 로드 (LocalStorage에서 언어 복원 → JSON 로드).</summary>
    Task InitializeAsync();

    /// <summary>언어 변경 시 호출 (컴포넌트들이 구독해 StateHasChanged 호출).</summary>
    event Action? OnLanguageChanged;
}
