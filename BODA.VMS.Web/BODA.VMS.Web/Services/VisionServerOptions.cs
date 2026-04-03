namespace BODA.VMS.Web.Services;

public class VisionServerOptions
{
    public const string SectionName = "VisionServer";
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = string.Empty;
}
