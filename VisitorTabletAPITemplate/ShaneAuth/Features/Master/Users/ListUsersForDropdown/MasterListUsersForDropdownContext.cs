using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDropdown
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MasterListUsersForDropdownRequest))]
    [JsonSerializable(typeof(SelectListWithImageResponse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class MasterListUsersForDropdownContext : JsonSerializerContext { }
}
