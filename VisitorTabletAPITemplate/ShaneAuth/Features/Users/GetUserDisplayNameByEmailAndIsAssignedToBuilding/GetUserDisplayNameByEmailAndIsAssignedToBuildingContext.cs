using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.GetUserDisplayNameByEmailAndIsAssignedToBuilding
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetUserDisplayNameByEmailAndIsAssignedToBuildingRequest))]
    [JsonSerializable(typeof(UserDisplayNameAndIsAssignedToBuildingData))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetUserDisplayNameByEmailAndIsAssignedToBuildingContext : JsonSerializerContext { }
}
