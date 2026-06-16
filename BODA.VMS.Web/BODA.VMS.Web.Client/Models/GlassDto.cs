namespace BODA.VMS.Web.Client.Models;

/// <summary>
/// 스마트 글라스 입고 위치 조회 응답.
/// 1차 목표(텍스트 표시)와 2차 목표(3D 하이라이트)를 한 번에 대비해
/// 위치 텍스트 + 좌표를 함께 내린다 (설계문서 §7 계약).
/// </summary>
public class InboundLocationDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;

    /// <summary>"Zone-Rack-Level-Bin" 형식의 사람이 읽는 위치 문자열.</summary>
    public string LocationText { get; set; } = string.Empty;

    /// <summary>2차 목표(3D 뷰어) 하이라이트용 좌표. 좌표 미설정 품목은 null 가능.</summary>
    public Coord3D? Coord { get; set; }
}

public class Coord3D
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}
