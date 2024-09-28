namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.EnableTwoFactorAuthentication
{
    public sealed class EnableTwoFactorAuthenticationRequest
    {
        public string? TotpCode { get; set; }
    }
}
