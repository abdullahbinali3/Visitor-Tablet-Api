using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using System.Text.Json.Serialization;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDataTable
{
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MasterListUsersForDataTableRequest))]
    [JsonSerializable(typeof(DataTableResponse<MasterUserDataForDataTable>))]
    [JsonSerializable(typeof(MyErrorResponse))]
    [JsonSerializable(typeof(MyInternalErrorResponse))]
    public partial class MasterListUsersForDataTableContext : JsonSerializerContext { }
}
