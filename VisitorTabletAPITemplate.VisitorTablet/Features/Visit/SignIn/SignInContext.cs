using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.SignIn
{

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(SignInRequest))]
    [JsonSerializable(typeof(List<SelectListItemGuid>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class SignInContext : JsonSerializerContext { }

}
