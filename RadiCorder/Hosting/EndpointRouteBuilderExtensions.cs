using RadiCorder.Endpoints;
using RadiCorder.Features.General;
using RadiCorder.Features.Notification;
using RadiCorder.Features.Program;
using RadiCorder.Features.Recording;
using RadiCorder.Features.Reserve;
using RadiCorder.Features.Setting;
using RadiCorder.Features.Tag;
using RadiCorder.Hubs;

namespace RadiCorder.Hosting;

/// <summary>
/// MVC、API、Hub の公開ルートを一箇所で登録する。
/// </summary>
public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapRadiCorderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapApiEndpoints();
        endpoints.MapGeneralEndpoints();
        endpoints.MapProgramEndpoints();
        endpoints.MapSettingEndpoints();
        endpoints.MapExternalImportEndpoints();
        endpoints.MapRecordingEndpoints();
        endpoints.MapNotificationEndpoints();
        endpoints.MapTagEndpoints();
        endpoints.MapReserveEndpoints();
        endpoints.MapHub<RecordingHub>("/hubs/recordings");
        endpoints.MapHub<NotificationHub>("/hubs/notifications");
        endpoints.MapHub<ReserveHub>("/hubs/reserves");
        endpoints.MapHub<ProgramUpdateHub>("/hubs/program-updates");
        endpoints.MapHub<AppEventHub>("/hubs/app-events");
        endpoints.MapHub<RecordedDuplicateDetectionHub>("/hubs/duplicate-detection");

        endpoints.MapControllerRoute(
            name: "areas",
            pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
        );

        endpoints.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}/{id?}");
        return endpoints;
    }
}
