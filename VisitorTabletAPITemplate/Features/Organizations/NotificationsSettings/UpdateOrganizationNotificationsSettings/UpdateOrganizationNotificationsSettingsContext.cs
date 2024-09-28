using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.NotificationsSettings.UpdateOrganizationNotificationsSettings
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateOrganizationNotificationsSettingsRequest))]
    [JsonSerializable(typeof(OrganizationNotificationsSetting))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class UpdateOrganizationNotificationsSettingsContext : JsonSerializerContext { }
}
