using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.RegisterVisitor
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(RegisterVisitorRequest))]
    [JsonSerializable(typeof(UserData))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class RegisterVisitorContext : JsonSerializerContext { }
}
