using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.FileStorage.Features.GetFileStorageLogByLogId
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetFileStorageLogByLogIdRequest))]
    [JsonSerializable(typeof(FileStorageLog))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetFileStorageLogByLogIdContext : JsonSerializerContext { }
}
