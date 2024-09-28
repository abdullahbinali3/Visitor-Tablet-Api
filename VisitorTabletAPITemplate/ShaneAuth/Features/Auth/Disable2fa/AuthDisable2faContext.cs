using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Disable2fa
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AuthDisable2faRequest))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class AuthDisable2faContext : JsonSerializerContext { }
}
