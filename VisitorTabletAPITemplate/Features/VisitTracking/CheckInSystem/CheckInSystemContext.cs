using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.Models;

namespace VisitorTabletAPITemplate.Features.VisitTracking.CheckInSystem
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(CheckInSystemRequest))]
    [JsonSerializable(typeof(Building))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class CheckInSystemContext : JsonSerializerContext { }
}
