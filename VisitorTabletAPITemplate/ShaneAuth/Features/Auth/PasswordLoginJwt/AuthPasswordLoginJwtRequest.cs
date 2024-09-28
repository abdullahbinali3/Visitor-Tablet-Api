namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.PasswordLoginJwt
{
    public sealed class AuthPasswordLoginJwtRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? TotpCode { get; set; }
    }
}
