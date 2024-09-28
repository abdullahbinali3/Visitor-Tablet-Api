using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.FileStorage.Features.ListFileStorageLogsByObjectIdForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListFileStorageLogsByObjectIdForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<FileStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListFileStorageLogsByObjectIdForDataTableContext : JsonSerializerContext { }
}
