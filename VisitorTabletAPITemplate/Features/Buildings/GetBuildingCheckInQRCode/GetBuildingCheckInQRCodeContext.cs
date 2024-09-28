using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Buildings.GetBuildingCheckInQRCode
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetBuildingCheckInQRCodeRequest))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetBuildingCheckInQRCodeContext : JsonSerializerContext { }
}
