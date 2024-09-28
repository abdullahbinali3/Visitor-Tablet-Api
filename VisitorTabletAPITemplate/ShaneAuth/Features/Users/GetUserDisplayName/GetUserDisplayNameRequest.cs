namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.GetUserDisplayName
{
    public sealed class GetUserDisplayNameRequest
    {
        public Guid? Uid { get; set; }
        public Guid? OrganizationId { get; set; }
    }
}
