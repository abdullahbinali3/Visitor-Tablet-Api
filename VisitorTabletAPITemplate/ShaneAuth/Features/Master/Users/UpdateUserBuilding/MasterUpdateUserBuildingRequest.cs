namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUserBuilding
{
    public sealed class MasterUpdateUserBuildingRequest
    {
        public Guid? Uid { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }

        // Building Details
        public Guid? FunctionId { get; set; }
        public List<Guid>? UserAssetTypes { get; set; }
        public bool? FirstAidOfficer { get; set; }
        public bool? FireWarden { get; set; }
        public bool? PeerSupportOfficer { get; set; }
        public bool? AllowBookingDeskForVisitor { get; set; }
        public bool? AllowBookingRestrictedRooms { get; set; }

        // Building Admin Details
        public List<Guid>? UserAdminFunctions { get; set; }
        public List<Guid>? UserAdminAssetTypes { get; set; }
        public bool? AllowBookingAnyoneAnywhere { get; set; }
    }
}
