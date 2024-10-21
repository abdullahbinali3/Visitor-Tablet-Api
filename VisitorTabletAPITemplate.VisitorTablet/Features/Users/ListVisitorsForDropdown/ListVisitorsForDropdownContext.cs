using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.User.ListVisitorsForDropdown
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListVisitorsForDropdownRequest))]
    [JsonSerializable(typeof(List<Models.Visitor>))]
    [JsonSerializable(typeof(Models.Visitor))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListVisitorsForDropdownContext : JsonSerializerContext { }
}
