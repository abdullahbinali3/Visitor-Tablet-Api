using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Regions.ListRegionsForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListRegionsForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<Region>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListRegionsForDataTableContext : JsonSerializerContext { }
}
