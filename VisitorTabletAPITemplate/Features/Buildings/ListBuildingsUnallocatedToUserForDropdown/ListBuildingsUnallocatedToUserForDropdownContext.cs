using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsUnallocatedToUserForDropdown
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListBuildingsUnallocatedToUserForDropdownRequest))]
    [JsonSerializable(typeof(SelectListResponse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListBuildingsUnallocatedToUserForDropdownContext : JsonSerializerContext { }
}
