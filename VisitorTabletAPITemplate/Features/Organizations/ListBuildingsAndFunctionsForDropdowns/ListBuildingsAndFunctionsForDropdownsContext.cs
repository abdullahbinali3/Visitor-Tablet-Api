using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFunctionsForDropdowns
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListBuildingsAndFunctionsForDropdownsRequest))]
    [JsonSerializable(typeof(ListBuildingsAndFunctionsForDropdownsResponse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListBuildingsAndFunctionsForDropdownsContext : JsonSerializerContext { }
}
