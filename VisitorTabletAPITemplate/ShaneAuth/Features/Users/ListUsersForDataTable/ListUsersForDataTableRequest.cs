using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListUsersForDataTable
{
    public sealed class ListUsersForDataTableRequest
    {
        public Guid? OrganizationId { get; set; }
        public bool? IncludeDisabled { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
