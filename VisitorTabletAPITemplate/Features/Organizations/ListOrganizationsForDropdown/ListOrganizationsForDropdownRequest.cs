namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsForDropdown
{
    public sealed class ListOrganizationsForDropdownRequest
    {
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
