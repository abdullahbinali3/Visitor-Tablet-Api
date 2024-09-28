using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ImageStorage.Features.ListImageStorageLogsByImageStorageIdForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListImageStorageLogsByImageStorageIdForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<ImageStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListImageStorageLogsByImageStorageIdForDataTableContext : JsonSerializerContext { }
}
