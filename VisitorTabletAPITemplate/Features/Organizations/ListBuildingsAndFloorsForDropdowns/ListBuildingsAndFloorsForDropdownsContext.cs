using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFloorsForDropdowns
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListBuildingsAndFloorsForDropdownsRequest))]
    [JsonSerializable(typeof(ListBuildingsAndFloorsForDropdownsResponse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListBuildingsAndFloorsForDropdownsContext : JsonSerializerContext { }
}
