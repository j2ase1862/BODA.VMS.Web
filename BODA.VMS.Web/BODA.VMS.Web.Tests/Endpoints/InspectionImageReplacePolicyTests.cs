using BODA.VMS.Web.Endpoints;
using FluentAssertions;

namespace BODA.VMS.Web.Tests.Endpoints;

/// <summary>
/// 검사 이미지 대체 정책 (2026-08-19, VMS 사이클 키 공유와 세트) —
/// 한 사이클의 여러 카메라 이미지가 같은 CorrelationKey 로 도착할 때,
/// NG 이력에는 NG 이미지가 우선해야 한다 (먼저 온 OK 이미지를 대체).
/// 그 외에는 첫 이미지 유지(재시도 멱등).
/// </summary>
public class InspectionImageReplacePolicyTests
{
    private const string OkPath = "/images/2026-08-19/OK/abc123.jpg";
    private const string NgPath = "/images/2026-08-19/NG/abc123.jpg";

    [Fact]
    public void NgHistory_ExistingOkImage_IncomingNg_Replaces()
        => InspectionImageEndpoints.ShouldReplaceExisting(OkPath, "NG", historyIsPass: false)
            .Should().BeTrue();

    [Fact]
    public void NgHistory_ExistingNgImage_IncomingNg_KeepsFirst()
        => InspectionImageEndpoints.ShouldReplaceExisting(NgPath, "NG", historyIsPass: false)
            .Should().BeFalse();

    [Fact]
    public void NgHistory_IncomingOk_NeverReplaces()
        => InspectionImageEndpoints.ShouldReplaceExisting(NgPath, "OK", historyIsPass: false)
            .Should().BeFalse();

    [Fact]
    public void PassHistory_NeverReplaces()
        => InspectionImageEndpoints.ShouldReplaceExisting(OkPath, "NG", historyIsPass: true)
            .Should().BeFalse();

    [Fact]
    public void MissingVerdict_KeepsFirst()
        => InspectionImageEndpoints.ShouldReplaceExisting(OkPath, null, historyIsPass: false)
            .Should().BeFalse();
}
