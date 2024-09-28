using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ImageStorage.Features.ListImageStorageLogsForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListImageStorageLogsForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<ImageStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListImageStorageLogsForDataTableContext : JsonSerializerContext { }
}
