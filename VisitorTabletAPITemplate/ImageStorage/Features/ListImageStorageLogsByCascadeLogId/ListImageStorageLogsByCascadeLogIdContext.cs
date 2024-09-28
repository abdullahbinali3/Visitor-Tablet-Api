using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ImageStorage.Features.ListImageStorageLogsByCascadeLogIdForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListImageStorageLogsByCascadeLogIdForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<ImageStorageLog>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListImageStorageLogsByCascadeLogIdForDataTableContext : JsonSerializerContext { }
}
