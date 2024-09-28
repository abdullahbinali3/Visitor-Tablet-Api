using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListUsersForBookAnyoneAnywhereForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(ListUsersForBookAnyoneAnywhereForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<ManageableUserDataForDataTable>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class ListUsersForBookAnyoneAnywhereForDataTableContext : JsonSerializerContext { }
}
