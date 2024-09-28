using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.FileStorage.Features.ListFileStorageLogsForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListFileStorageLogsForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<FileStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListFileStorageLogsForDataTableContext : JsonSerializerContext { }
}
