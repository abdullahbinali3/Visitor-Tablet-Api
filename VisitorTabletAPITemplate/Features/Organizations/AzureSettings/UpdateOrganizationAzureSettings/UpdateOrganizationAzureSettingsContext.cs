using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.AzureSettings.UpdateOrganizationAzureSettings
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateOrganizationAzureSettingsRequest))]
    [JsonSerializable(typeof(OrganizationAzureSetting))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class UpdateOrganizationAzureSettingsContext : JsonSerializerContext { }
}
