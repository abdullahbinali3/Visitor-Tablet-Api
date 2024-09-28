using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.LinkAccountAzureAD
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AuthLinkAccountAzureADRequest))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class AuthLinkAccountAzureADContext : JsonSerializerContext { }
}
