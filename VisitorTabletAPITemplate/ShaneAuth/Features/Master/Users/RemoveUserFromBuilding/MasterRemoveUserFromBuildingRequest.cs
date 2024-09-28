namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromBuilding
{
    public sealed class MasterRemoveUserFromBuildingRequest
    {
        public Guid? Uid { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
