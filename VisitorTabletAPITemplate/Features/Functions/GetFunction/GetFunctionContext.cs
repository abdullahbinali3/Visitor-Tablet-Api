using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Functions.GetFunction
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(GetFunctionRequest))]
    [JsonSerializable(typeof(Function))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class GetFunctionContext : JsonSerializerContext { }
}
