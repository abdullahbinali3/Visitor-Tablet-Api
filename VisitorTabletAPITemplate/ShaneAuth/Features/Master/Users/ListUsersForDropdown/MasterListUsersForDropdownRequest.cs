namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDropdown
{
    public sealed class MasterListUsersForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public bool? IncludeDisabled { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
