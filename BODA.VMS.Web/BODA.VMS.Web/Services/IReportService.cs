using BODA.VMS.Web.Client.Models;

namespace BODA.VMS.Web.Services;

public interface IReportService
{
    /// <summary>PDF 바이트 + 다운로드 파일명 반환</summary>
    Task<(byte[] Pdf, string FileName)> GenerateAsync(ReportRequestDto request);
}
