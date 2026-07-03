namespace BODA.VMS.Web.Services;

/// <summary>
/// 이미 사용 중인 ClientIndex 로 클라이언트를 생성/수정하려 할 때 발생.
/// 엔드포인트에서 409 Conflict 로 매핑된다.
/// </summary>
public sealed class DuplicateClientIndexException : Exception
{
    public int ClientIndex { get; }

    public DuplicateClientIndexException(int clientIndex)
        : base($"ClientIndex {clientIndex} 는 이미 다른 클라이언트에 사용 중입니다. 라인마다 고유한 인덱스를 사용하세요.")
    {
        ClientIndex = clientIndex;
    }
}
