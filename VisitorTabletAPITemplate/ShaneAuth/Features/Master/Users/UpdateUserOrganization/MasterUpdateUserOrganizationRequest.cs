using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUserOrganization
{
    public sealed class MasterUpdateUserOrganizationRequest
    {
        public Guid? Uid { get; set; }
        public Guid? OrganizationId { get; set; }
        public UserOrganizationRole? UserOrganizationRole { get; set; }
        public string? Note { get; set; }
        public bool? Contractor { get; set; }
        public bool? Visitor { get; set; }
        public bool? UserOrganizationDisabled { get; set; }
    }
}
