namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Register.InitRegister
{
    public sealed class AuthInitRegisterRequest
    {
        public string? Email { get; set; }
        public string? UserAgentBrowserName { get; set; }
        public string? UserAgentOsName { get; set; }
        public string? UserAgentDeviceInfo { get; set; }
    }
}
