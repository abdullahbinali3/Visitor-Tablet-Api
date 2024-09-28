namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsUnallocatedToUserForDropdown
{
    public sealed class ListBuildingsUnallocatedToUserForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? Uid { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
