using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Functions.ListFunctionsForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListFunctionsForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<Function>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListFunctionsForDataTableContext : JsonSerializerContext { }
}
