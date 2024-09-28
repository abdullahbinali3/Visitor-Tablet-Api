using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingTimezones
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListBuildingTimezonesRequest))]
    [JsonSerializable(typeof(Dictionary<Guid, string>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListBuildingTimezonesContext : JsonSerializerContext { }
}
