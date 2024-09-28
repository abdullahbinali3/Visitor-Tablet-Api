using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Regions.DeleteRegion
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DeleteRegionRequest))]
    [JsonSerializable(typeof(Region))]
    [JsonSerializable(typeof(DeleteRegionResponse_RegionInUse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class DeleteRegionContext : JsonSerializerContext { }
}
