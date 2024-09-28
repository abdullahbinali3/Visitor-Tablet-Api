using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitDisableTwoFactorAuthentication
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(InitDisableTwoFactorAuthenticationRequest))]
    [JsonSerializable(typeof(UserData))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class InitDisableTwoFactorAuthenticationContext : JsonSerializerContext { }
}
