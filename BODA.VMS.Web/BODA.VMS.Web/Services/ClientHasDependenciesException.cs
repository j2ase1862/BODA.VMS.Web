namespace BODA.VMS.Web.Services;

/// <summary>
/// 검사 이력 등 자식 데이터가 남아 있는 클라이언트를 삭제하려 할 때 발생.
/// 추적성 데이터(검사 이력·작업지시 등)는 보존 대상이므로 삭제 대신 비활성화를 안내한다.
/// 엔드포인트에서 409 Conflict 로 매핑된다.
/// </summary>
public sealed class ClientHasDependenciesException : Exception
{
    public string ClientName { get; }
    public IReadOnlyList<string> Dependencies { get; }

    public ClientHasDependenciesException(string clientName, IReadOnlyList<string> dependencies)
        : base($"클라이언트 '{clientName}' 에 연결된 데이터({string.Join(", ", dependencies)})가 있어 삭제할 수 없습니다. " +
               "이력 데이터 보존을 위해 삭제 대신 비활성화를 사용하세요.")
    {
        ClientName = clientName;
        Dependencies = dependencies;
    }
}
