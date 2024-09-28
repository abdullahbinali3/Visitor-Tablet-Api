namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.CheckForgotPassword
{
    public sealed class AuthCheckForgotPasswordRequest
    {
        public Guid? Uid { get; set; }
        public string? Token { get; set; }
    }
}
