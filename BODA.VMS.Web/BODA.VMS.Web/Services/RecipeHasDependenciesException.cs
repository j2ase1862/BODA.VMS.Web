namespace BODA.VMS.Web.Services;

/// <summary>
/// 작업지시 등 자식 데이터가 참조하는 레시피를 삭제하려 할 때 발생.
/// 생산 이력의 추적성을 위해 참조 중인 작업지시가 있으면 삭제를 차단한다.
/// 엔드포인트에서 409 Conflict 로 매핑된다. (ClientHasDependenciesException 과 동일 패턴)
/// </summary>
public sealed class RecipeHasDependenciesException : Exception
{
    public string RecipeName { get; }
    public IReadOnlyList<string> Dependencies { get; }

    public RecipeHasDependenciesException(string recipeName, IReadOnlyList<string> dependencies)
        : base($"레시피 '{recipeName}' 을(를) 참조하는 데이터({string.Join(", ", dependencies)})가 있어 삭제할 수 없습니다. " +
               "해당 작업지시를 먼저 종료/삭제한 뒤 다시 시도하세요.")
    {
        RecipeName = recipeName;
        Dependencies = dependencies;
    }
}
