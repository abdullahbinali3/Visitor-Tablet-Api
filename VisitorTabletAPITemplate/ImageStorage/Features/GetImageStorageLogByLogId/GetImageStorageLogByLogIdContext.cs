using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ImageStorage.Features.GetImageStorageLogByLogId
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetImageStorageLogByLogIdRequest))]
    [JsonSerializable(typeof(ImageStorageLog))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetImageStorageLogByLogIdContext : JsonSerializerContext { }
}
