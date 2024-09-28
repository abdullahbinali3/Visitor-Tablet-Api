using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsForDataTable
{
    public sealed class ListOrganizationsForDataTableRequest
    {
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
