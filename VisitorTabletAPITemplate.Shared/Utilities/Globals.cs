using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.Utilities
{
    public static class Globals
    {
        public static List<EndpointRouteInfo> Endpoints { get; set; } = new List<EndpointRouteInfo>();
        public static string MachineName { get; set; } = default!;
        public static string FrontEndBaseUrl { get; set; } = default!;
        public static string BackEndBaseUrl { get; set; } = default!;
        public static string HotDeskHtmlColor { get; } = "#f87171";
        public static DateTime EndOfTheWorldUtc { get; } = new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
