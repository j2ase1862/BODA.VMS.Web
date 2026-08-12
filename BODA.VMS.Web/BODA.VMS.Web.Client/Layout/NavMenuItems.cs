namespace BODA.VMS.Web.Client.Layout;

/// <summary>
/// 상단 내비(TopNav, 데스크톱)와 드로어(NavMenu, 모바일 폴백)가 공유하는 메뉴 정의.
/// 3a 리디자인(2026-08-12) — 항목·권한·i18n 키를 한 곳에서 관리해 두 컴포넌트의 불일치를 방지.
/// </summary>
public sealed record NavItemDef(string TitleKey, string Href, string Icon, bool NewTab = false, string? Roles = null);

public sealed record NavGroupDef(string Key, string TitleKey, string Icon, IReadOnlyList<NavItemDef> Items, string? Roles = null);

public static class NavMenuItems
{
    /// <summary>대시보드 — TopNav에서는 직접 pill로 승격되어 모니터링 드롭다운에서 제외된다.</summary>
    public static readonly NavItemDef Dashboard =
        new("menu.dashboard", "/", MudBlazor.Icons.Material.Filled.GridView);

    public static readonly IReadOnlyList<NavGroupDef> Groups = new[]
    {
        new NavGroupDef("monitoring", "menu.monitoring", MudBlazor.Icons.Material.Filled.Visibility, new[]
        {
            Dashboard,
            new NavItemDef("menu.andon", "/andon", MudBlazor.Icons.Material.Filled.Tv, NewTab: true),
            new NavItemDef("menu.alarms", "/alarms", MudBlazor.Icons.Material.Filled.NotificationImportant),
            new NavItemDef("menu.history", "/history", MudBlazor.Icons.Material.Filled.Assessment),
        }),
        new NavGroupDef("production", "menu.production", MudBlazor.Icons.Material.Filled.Factory, new[]
        {
            new NavItemDef("menu.workorders", "/workorders", MudBlazor.Icons.Material.Filled.Assignment),
            new NavItemDef("menu.products", "/products", MudBlazor.Icons.Material.Filled.Inventory),
            new NavItemDef("menu.operators", "/operators", MudBlazor.Icons.Material.Filled.Badge),
        }),
        new NavGroupDef("quality", "menu.quality", MudBlazor.Icons.Material.Filled.VerifiedUser, new[]
        {
            // 검사항목(레시피 파라미터) = 품질 기준정보 — 검사 기준값은 Guest 비노출 (Admin/User 조회, 편집 Admin)
            new NavItemDef("menu.inspectionItems", "/inspection-items", MudBlazor.Icons.Material.Filled.Rule, Roles: "Admin,User"),
            new NavItemDef("menu.paretoAnalysis", "/quality-analysis", MudBlazor.Icons.Material.Filled.Analytics),
            new NavItemDef("menu.spc", "/spc", MudBlazor.Icons.Material.Filled.ShowChart),
            new NavItemDef("menu.defectCodes", "/defect-codes", MudBlazor.Icons.Material.Filled.BugReport),
            new NavItemDef("menu.shiftReport", "/shift-report", MudBlazor.Icons.Material.Filled.Schedule),
            new NavItemDef("menu.pdfReports", "/reports", MudBlazor.Icons.Material.Filled.PictureAsPdf),
        }),
        new NavGroupDef("equipment", "menu.equipment", MudBlazor.Icons.Material.Filled.PrecisionManufacturing, new[]
        {
            new NavItemDef("menu.clients", "/clients", MudBlazor.Icons.Material.Filled.Router),
            new NavItemDef("menu.oee", "/oee", MudBlazor.Icons.Material.Filled.Speed),
            new NavItemDef("menu.reliability", "/reliability", MudBlazor.Icons.Material.Filled.Insights),
            new NavItemDef("menu.maintenance", "/maintenance", MudBlazor.Icons.Material.Filled.Build),
            new NavItemDef("menu.forecast", "/forecast", MudBlazor.Icons.Material.Filled.TrendingUp),
        }),
        new NavGroupDef("admin", "menu.system", MudBlazor.Icons.Material.Filled.Settings, new[]
        {
            new NavItemDef("menu.shifts", "/shifts", MudBlazor.Icons.Material.Filled.WatchLater),
            new NavItemDef("menu.approvals", "/admin/approvals", MudBlazor.Icons.Material.Filled.VerifiedUser),
            new NavItemDef("menu.auditLogs", "/audit-logs", MudBlazor.Icons.Material.Filled.History),
            new NavItemDef("menu.settings", "/settings", MudBlazor.Icons.Material.Filled.Tune),
        }, Roles: "Admin"),
    };
}
