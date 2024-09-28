using VisitorTabletAPITemplate.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateLastUsedBuilding
{
    public sealed class UserUpdateLastUsedBuildingRequest
    {
        public int? ClientPortalType { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
    }
}
