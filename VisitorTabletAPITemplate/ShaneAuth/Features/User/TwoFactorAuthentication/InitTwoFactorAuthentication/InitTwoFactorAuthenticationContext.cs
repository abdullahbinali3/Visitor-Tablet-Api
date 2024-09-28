using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitTwoFactorAuthentication
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(InitTwoFactorAuthenticationResponse))]
    [JsonSerializable(typeof(UserData))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class InitTwoFactorAuthenticationContext : JsonSerializerContext { }
}
