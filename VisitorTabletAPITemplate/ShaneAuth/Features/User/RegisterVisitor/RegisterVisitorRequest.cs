namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.RegisterVisitor
{
    public sealed class RegisterVisitorRequest
    {
        public Guid WorkplaceVisitId { get; set; } 
        public Guid Uid { get; set; }
        public Guid BuildingId { get; set; }
        public Guid HostUid { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string Surname { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? MobileNumber { get; set; }
        public string Purpose { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public DateTime? SignInDateUtc { get; set; }
        public DateTime? SignOutDateUtc { get; set; }
        public DateTime StartDateUtc { get; set; }
        public DateTime EndDateUtc { get; set; }
    }

}
