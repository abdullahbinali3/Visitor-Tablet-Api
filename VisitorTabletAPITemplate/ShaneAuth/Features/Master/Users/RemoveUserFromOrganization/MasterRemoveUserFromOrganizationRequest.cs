namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromOrganization
{
    public sealed class MasterRemoveUserFromOrganizationRequest
    {
        public Guid? Uid { get; set; }
        public Guid? OrganizationId { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
