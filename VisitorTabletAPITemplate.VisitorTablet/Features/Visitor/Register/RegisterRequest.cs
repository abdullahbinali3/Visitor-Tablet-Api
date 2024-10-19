namespace VisitorTabletAPITemplate.VisitorTablet.Features.Visitor.Register
{
    public sealed class RegisterRequest
    {
        public Guid WorkplaceVisitId { get; set; }
        public Guid FormCompletedByUid { get; set; }
        public Guid BuildingId { get; set; }
        public Guid OrganizationId { get; set; }
        public Guid HostUid { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public DateTime? SignInDateUtc { get; set; }
        public DateTime? SignOutDateUtc { get; set; }
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
        public List<UserInfo> Users { get; set; } = new List<UserInfo>();
    }

    // UserInfo class to represent user details
    public sealed class UserInfo
    {
        public Guid Uid { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
    }
}
