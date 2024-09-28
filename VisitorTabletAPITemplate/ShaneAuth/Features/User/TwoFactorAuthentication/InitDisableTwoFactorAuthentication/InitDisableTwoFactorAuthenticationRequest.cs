namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitDisableTwoFactorAuthentication
{
    public sealed class InitDisableTwoFactorAuthenticationRequest
    {
        public string? Password { get; set; }
        public string? UserAgentBrowserName { get; set; }
        public string? UserAgentOsName { get; set; }
        public string? UserAgentDeviceInfo { get; set; }
    }
}
