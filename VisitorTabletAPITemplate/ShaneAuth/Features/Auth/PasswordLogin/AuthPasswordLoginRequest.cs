namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.PasswordLogin
{
    public sealed class AuthPasswordLoginRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? TotpCode { get; set; }
    }
}
