namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.RevokeForgotPassword
{
    public sealed class AuthRevokeForgotPasswordRequest
    {
        public Guid? Uid { get; set; }
        public Guid? Token { get; set; }
    }
}
