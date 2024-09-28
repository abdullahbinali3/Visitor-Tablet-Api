namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsUnallocatedToUserForDropdown
{
    public sealed class ListOrganizationsUnallocatedToUserForDropdownRequest
    {
        public Guid? Uid { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
