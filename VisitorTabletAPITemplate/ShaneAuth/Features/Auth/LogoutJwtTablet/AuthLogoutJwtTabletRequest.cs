namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.LogoutJwtTablet
{
    public sealed class LogoutJwtTabletRequest
    {
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? TotpCode { get; set; }
    }
}
