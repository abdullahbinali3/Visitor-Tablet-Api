namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsForDropdown
{
    public sealed class ListBuildingsForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
