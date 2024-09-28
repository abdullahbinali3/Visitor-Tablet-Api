namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.CompleteForgotPassword
{
    public sealed class AuthCompleteForgotPasswordRequest
    {
        public Guid? Uid { get; set; }
        public string? Token { get; set; }
        public string? NewPassword { get; set; }
        public string? NewPasswordConfirm { get; set; }
    }
}
