namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListManageableUsersForDropdown
{
    public sealed class ListManageableUsersForDropdownRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public bool? IncludeDisabled { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
