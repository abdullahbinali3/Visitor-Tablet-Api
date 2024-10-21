using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignIn
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateWorkplaceVisitUserSignInRequest))]
    [JsonSerializable(typeof(List<SelectListItemGuid>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class UpdateWorkplaceVisitUserSignInContext : JsonSerializerContext { }
}
