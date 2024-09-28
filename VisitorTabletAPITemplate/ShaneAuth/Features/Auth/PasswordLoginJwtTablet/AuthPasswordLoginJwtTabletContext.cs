using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.PasswordLoginJwtTablet
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AuthPasswordLoginJwtTabletRequest))]
    [JsonSerializable(typeof(TokenResponse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class AuthPasswordLoginJwtTabletContext : JsonSerializerContext { }
}
