namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.PasswordLoginJwtTablet
{
    public sealed class AuthPasswordLoginJwtTabletRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? TotpCode { get; set; }
    }
}
