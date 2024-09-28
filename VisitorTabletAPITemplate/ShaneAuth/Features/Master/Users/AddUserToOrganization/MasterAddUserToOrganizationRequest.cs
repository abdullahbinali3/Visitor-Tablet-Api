using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.AddUserToOrganization
{
    public class MasterAddUserToOrganizationRequest
    {
        public Guid? Uid { get; set; }

        // Organization Details
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public UserOrganizationRole? UserOrganizationRole { get; set; }
        public bool? Contractor { get; set; }
        public bool? Visitor { get; set; }
        public string? Note { get; set; }
        public bool? UserOrganizationDisabled { get; set; }

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
