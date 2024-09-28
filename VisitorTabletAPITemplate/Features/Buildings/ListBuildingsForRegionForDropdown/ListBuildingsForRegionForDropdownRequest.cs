namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsForRegionForDropdown
{
    public sealed class ListBuildingsForRegionForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? RegionId { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
