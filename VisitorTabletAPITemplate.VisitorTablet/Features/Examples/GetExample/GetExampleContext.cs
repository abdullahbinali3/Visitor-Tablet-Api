using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.VisitorTablet.Models;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Examples.GetExample
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetExampleRequest))]
    [JsonSerializable(typeof(Example))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetExampleContext : JsonSerializerContext { }
}
