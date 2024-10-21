using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.VisitorTablet.Models;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Get
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetVisitorsRequest))]
    [JsonSerializable(typeof(List<Models.Visitor>))]
    [JsonSerializable(typeof(Models.Visitor))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetVisitorsContext : JsonSerializerContext { }
}
