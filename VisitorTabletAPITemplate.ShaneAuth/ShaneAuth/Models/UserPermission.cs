using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.Models
{
    /// <summary>
    /// This class contains only permission-related properties from tblUserOrganizationJoin
    /// </summary>
    public sealed class UserOrganizationPermission
    {
        public Guid Uid { get; set; }
        public Guid OrganizationId { get; set; }
        /// <summary>
        /// True if the Disabled = 1 in tblOrganizations
        /// </summary>
        public bool OrganizationDisabled { get; set; }
        public UserOrganizationRole UserOrganizationRole { get; set; }
        public UserSystemRole UserSystemRole { get; set; }
        /// <summary>
        /// This dictionary contains permissions from tblUserBuildingJoin
        /// </summary>
        public Dictionary<Guid, UserBuildingPermission> BuildingPermissions { get; set; } = new Dictionary<Guid, UserBuildingPermission>();
    }

    /// <summary>
    /// This class contains only permission-related properties from tblUserBuildingJoin
    /// </summary>
    public sealed class UserBuildingPermission
    {
        public Guid Uid { get; set; }
        public Guid BuildingId { get; set; }
        public string BuildingTimezone { get; set; } = default!;
        public Guid OrganizationId { get; set; }
        /// <summary>
        /// True if the Disabled = 1 in tblOrganizations
        /// </summary>
        public bool OrganizationDisabled { get; set; }
        public Guid FunctionId { get; set; }
        public bool AllowBookingDeskForVisitor { get; set; }
        public bool AllowBookingRestrictedRooms { get; set; }
        public bool AllowBookingAnyoneAnywhere { get; set; }
        public UserOrganizationPermission UserOrganizationPermission { get; set; } = default!;
    }
}
