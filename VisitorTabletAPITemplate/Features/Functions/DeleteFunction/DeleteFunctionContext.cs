using VisitorTabletAPITemplate.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Functions.DeleteFunction
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(DeleteFunctionRequest))]
    [JsonSerializable(typeof(Function))]
    [JsonSerializable(typeof(DeleteFunctionResponse_FunctionInUse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class DeleteFunctionContext : JsonSerializerContext { }
}
