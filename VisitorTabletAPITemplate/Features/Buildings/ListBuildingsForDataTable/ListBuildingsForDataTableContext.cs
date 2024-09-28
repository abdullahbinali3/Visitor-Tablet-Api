using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListBuildingsForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<Building>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListBuildingsForDataTableContext : JsonSerializerContext { }
}
