using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisits.CreateWorkplaceVisit
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(CreateWorkplaceVisitRequest))]
    [JsonSerializable(typeof(List<SelectListItemGuid>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class CreateWorkplaceVisitContext : JsonSerializerContext { }
}
