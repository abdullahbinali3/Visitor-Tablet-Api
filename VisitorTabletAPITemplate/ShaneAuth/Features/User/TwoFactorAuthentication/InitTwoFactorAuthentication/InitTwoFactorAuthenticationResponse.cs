namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitTwoFactorAuthentication
{
    // NOTE: This is the response class - not request.
    public sealed class InitTwoFactorAuthenticationResponse
    {
        public string OtpUriString { get; set; } = default!;
        public string Secret { get; set; } = default!;
        public int Digits { get; set; }
        public int Period { get; set; }
        public string HashFunction { get; set; } = default!;
    }
}
