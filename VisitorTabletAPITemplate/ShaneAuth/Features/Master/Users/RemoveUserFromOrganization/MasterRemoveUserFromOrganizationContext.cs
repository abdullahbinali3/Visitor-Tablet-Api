using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromOrganization
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MasterRemoveUserFromOrganizationRequest))]
    [JsonSerializable(typeof(UserData))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class MasterRemoveUserFromOrganizationContext : JsonSerializerContext { }
}
