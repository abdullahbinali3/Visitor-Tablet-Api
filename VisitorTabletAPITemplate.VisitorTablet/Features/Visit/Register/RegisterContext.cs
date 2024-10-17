using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Register
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(RegisterRequest))]
    [JsonSerializable(typeof(List<SelectListItemGuid>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class RegisterContext : JsonSerializerContext { }
}
