using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.CompleteForgotPassword
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(AuthCompleteForgotPasswordRequest))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class AuthCompleteForgotPasswordContext : JsonSerializerContext { }
}
