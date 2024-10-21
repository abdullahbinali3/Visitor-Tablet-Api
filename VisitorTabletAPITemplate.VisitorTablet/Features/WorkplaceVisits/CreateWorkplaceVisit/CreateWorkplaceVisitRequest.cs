namespace VisitorTabletAPITemplate.VisitorTablet.Features.WorkplaceVisits.CreateWorkplaceVisit
{
    public sealed class CreateWorkplaceVisitRequest
    {
        public Guid BuildingId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid HostUid { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<UserInfo> Users { get; set; } = new List<UserInfo>();
    }

    // UserInfo class to represent user details
    public sealed class UserInfo
    {
        public string FirstName { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
    }
}
