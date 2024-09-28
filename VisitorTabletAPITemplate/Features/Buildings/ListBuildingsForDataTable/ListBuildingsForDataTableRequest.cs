using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsForDataTable
{
    public sealed class ListBuildingsForDataTableRequest
    {
        public Guid? OrganizationId { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
