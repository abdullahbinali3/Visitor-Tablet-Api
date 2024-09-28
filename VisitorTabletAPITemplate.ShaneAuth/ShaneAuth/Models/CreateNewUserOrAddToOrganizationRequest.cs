using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class CreateNewUserOrAddToOrganizationRequest
    {
        // User details
        public required string Email { get; set; }
        public required string FirstName { get; set; }
        public required string? Surname { get; set; }
        public string? Password { get; set; }
        public required UserSystemRole UserSystemRole { get; set; }
        public string? Timezone { get; set; }
        public required bool Disabled { get; set; }

        // Organization Details
        public required Guid OrganizationId { get; set; }
        public required Guid BuildingId { get; set; }
        public required UserOrganizationRole UserOrganizationRole { get; set; }
        public required bool Contractor { get; set; }
        public required bool Visitor { get; set; }
        public string? Note { get; set; }
        public required bool UserOrganizationDisabled { get; set; }

        // Building Details
        public required Guid FunctionId { get; set; }
        public List<Guid>? UserAssetTypes { get; set; }
        public required bool FirstAidOfficer { get; set; }
        public required bool FireWarden { get; set; }
        public required bool PeerSupportOfficer { get; set; }
        public required bool AllowBookingDeskForVisitor { get; set; }
        public required bool AllowBookingRestrictedRooms { get; set; }

        // Building Admin Details
        public List<Guid>? UserAdminFunctions { get; set; }
        public List<Guid>? UserAdminAssetTypes { get; set; }
        public required bool AllowBookingAnyoneAnywhere { get; set; }
    }
}
