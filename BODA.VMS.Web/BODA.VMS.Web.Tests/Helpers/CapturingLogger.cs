using Microsoft.Extensions.Logging;

namespace BODA.VMS.Web.Tests.Helpers;

/// <summary>
/// 로그 메시지를 리스트로 캡처하는 테스트용 ILogger — 진단 경고가 실제로
/// 발생했는지(레벨 + 메시지 내용) 단언할 때 사용. NullLogger 로 충분한
/// 테스트에는 쓰지 말 것.
/// </summary>
public sealed class CapturingLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));
}
