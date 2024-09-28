using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.Features.Functions.ListFunctionsForDataTable
{
    public sealed class ListFunctionsForDataTableRequest
    {
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public SortType? Sort { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
