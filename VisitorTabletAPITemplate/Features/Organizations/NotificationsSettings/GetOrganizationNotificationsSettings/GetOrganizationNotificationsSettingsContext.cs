using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.NotificationsSettings.GetOrganizationNotificationsSettings
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(OrganizationNotificationsSetting))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetOrganizationNotificationsSettingsContext : JsonSerializerContext { }
}
