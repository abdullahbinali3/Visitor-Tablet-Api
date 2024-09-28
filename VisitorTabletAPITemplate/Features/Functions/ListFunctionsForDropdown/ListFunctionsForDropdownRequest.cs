namespace VisitorTabletAPITemplate.Features.Functions.ListFunctionsForDropdown
{
    public sealed class ListFunctionsForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
