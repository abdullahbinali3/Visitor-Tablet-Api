using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ShaneAuth.Models;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.SignIn
{
    
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(SignInRequest))]
    [JsonSerializable(typeof(UserData))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class SignInContext : JsonSerializerContext { }

}
