using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUser
{
    public sealed class MasterUpdateUserRequest
    {
        public Guid? Uid { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string? NewPassword { get; set; }
        public string? NewPasswordConfirm { get; set; }
        public UserSystemRole? UserSystemRole { get; set; }
        public string? Timezone { get; set; }
        public bool? Disabled { get; set; }
        public bool? AvatarImageChanged { get; set; }
        public IFormFile? AvatarImage { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
