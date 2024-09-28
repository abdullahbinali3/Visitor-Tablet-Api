using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDataTable
{
    public sealed class MasterListUsersForDataTableRequest
    {
        public MasterListUsersForDataTableRequest_Filter? Filter { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
    
    public sealed class MasterListUsersForDataTableRequest_Filter
    {
        public List<Guid>? OrganizationIds { get; set; }
        public bool? Enabled { get; set; }
        public int? UserSystemRole { get; set; }
    }
}
