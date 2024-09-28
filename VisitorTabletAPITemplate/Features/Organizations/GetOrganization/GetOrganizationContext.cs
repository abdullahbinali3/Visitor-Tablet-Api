using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.GetOrganization
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetOrganizationRequest))]
    [JsonSerializable(typeof(Organization))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetOrganizationContext : JsonSerializerContext { }
}
