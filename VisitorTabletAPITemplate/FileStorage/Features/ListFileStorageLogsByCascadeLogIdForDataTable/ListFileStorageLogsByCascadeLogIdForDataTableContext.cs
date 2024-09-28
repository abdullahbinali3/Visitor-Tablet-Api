using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.FileStorage.Features.ListFileStorageLogsByCascadeLogIdForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListFileStorageLogsByCascadeLogIdForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<FileStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListFileStorageLogsByCascadeLogIdForDataTableContext : JsonSerializerContext { }
}
