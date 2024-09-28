namespace VisitorTabletAPITemplate.Features.Regions.ListRegionsForDropdown
{
    public sealed class ListRegionsForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
