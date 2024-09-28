namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.InitForgotPassword
{
    public sealed class AuthInitForgotPasswordRequest
    {
        public string? Email { get; set; }
        public string? UserAgentBrowserName { get; set; }
        public string? UserAgentOsName { get; set; }
        public string? UserAgentDeviceInfo { get; set; }
    }
}
