namespace BODA.VMS.Web.Client.Utils;

/// <summary>
/// 날짜 범위 필터 → 서버 쿼리 값 변환 공용 헬퍼.
/// 서버 저장이 UTC(<c>DateTime.UtcNow</c>)인 컬럼 전용 — 로컬(<c>DateTime.Now</c>) 저장
/// 컬럼에 적용하면 반대 방향으로 9시간 밀린다. 적용 전 해당 컬럼의 기록 지점을 확인할 것.
/// (배경: 필터를 로컬 시각 그대로 보내면 KST 하루 조회가 실제로는 당일 09:00~익일 08:59
/// 구간이 된다 — 2026-08-21 현장)
/// </summary>
public static class QueryDate
{
    /// <summary>선택한 날짜의 로컬 00:00:00 → UTC.</summary>
    public static DateTime? StartUtc(DateTime? localDate)
        => localDate?.Date.ToUniversalTime();

    /// <summary>
    /// 선택한 날짜의 로컬 23:59:59 → UTC. 종료일을 하루 끝까지 포함시켜
    /// 시작=종료(하루치) 조회가 0폭 구간이 되지 않게 한다.
    /// </summary>
    public static DateTime? EndUtc(DateTime? localDate)
        => localDate?.Date.AddDays(1).AddSeconds(-1).ToUniversalTime();
}
