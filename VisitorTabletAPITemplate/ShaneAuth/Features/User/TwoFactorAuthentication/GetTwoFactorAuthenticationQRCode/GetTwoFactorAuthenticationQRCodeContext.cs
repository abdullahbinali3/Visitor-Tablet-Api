using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.GetTwoFactorAuthenticationQRCode
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetTwoFactorAuthenticationQRCodeContext : JsonSerializerContext { }
}
