namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateProfile
{
    public sealed class UserUpdateProfileRequest
    {
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public bool? AvatarImageChanged { get; set; }
        public IFormFile? AvatarImage { get; set; }
        public string? Timezone { get; set; }
        public bool? UserHasNoPassword { get; set; }
        public string? CurrentPassword { get; set; }
        public string? NewPassword { get; set; }
        public string? NewPasswordConfirm { get; set; }

        // User Organization Note
        public Guid? OrganizationId { get; set; }
        public string? Note { get; set; }
    }
}
