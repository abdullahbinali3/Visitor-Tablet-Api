namespace VisitorTabletAPITemplate.VisitorTablet.Features.User.ListHostsForDropdown
{
    public sealed class ListHostsForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public bool? IncludeDisabled { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
