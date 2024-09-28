using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.AzureSettings.GetOrganizationAzureSettings
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(OrganizationAzureSetting))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetOrganizationAzureSettingsContext : JsonSerializerContext { }
}
