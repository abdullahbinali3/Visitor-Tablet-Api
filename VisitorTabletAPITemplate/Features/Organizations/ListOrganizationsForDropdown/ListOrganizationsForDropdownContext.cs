using VisitorTabletAPITemplate.ObjectClasses;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsForDropdown
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListOrganizationsForDropdownRequest))]
    [JsonSerializable(typeof(SelectListResponse))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListOrganizationsForDropdownContext : JsonSerializerContext { }
}
