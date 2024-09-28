namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.GetUserDisplayNameByEmailAndIsAssignedToBuilding
{
    public sealed class GetUserDisplayNameByEmailAndIsAssignedToBuildingRequest
    {
        public string? Email { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
    }
}
