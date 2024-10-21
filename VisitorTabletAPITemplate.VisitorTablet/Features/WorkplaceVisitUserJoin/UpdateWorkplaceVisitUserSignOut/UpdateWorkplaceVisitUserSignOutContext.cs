using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisitUserJoin.UpdateWorkplaceVisitUserSignOut
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(UpdateWorkplaceVisitUserSignOutRequest))]
    [JsonSerializable(typeof(List<SelectListItemGuid>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class UpdateWorkplaceVisitUserSignOutContext : JsonSerializerContext { }
}
