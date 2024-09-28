using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListManageableUsersForDataTable
{
    public sealed class ListManageableUsersForDataTableRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public bool? IncludeDisabled { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
