using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsUnallocatedToUserForDataTable
{
    public sealed class ListBuildingsUnallocatedToUserForDataTableRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? Uid { get; set; }
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
