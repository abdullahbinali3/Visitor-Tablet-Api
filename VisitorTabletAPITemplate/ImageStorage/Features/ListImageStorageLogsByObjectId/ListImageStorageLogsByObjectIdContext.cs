using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ImageStorage.Features.ListImageStorageLogsByObjectIdForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListImageStorageLogsByObjectIdForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<ImageStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListImageStorageLogsByObjectIdForDataTableContext : JsonSerializerContext { }
}
