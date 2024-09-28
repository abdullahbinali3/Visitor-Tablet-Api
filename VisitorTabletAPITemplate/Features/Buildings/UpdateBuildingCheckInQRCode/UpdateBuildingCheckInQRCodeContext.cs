using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Buildings.UpdateBuildingCheckInQRCode
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateBuildingCheckInQRCodeRequest))]
    [JsonSerializable(typeof(Building))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class UpdateBuildingCheckInQRCodeContext : JsonSerializerContext { }
}
