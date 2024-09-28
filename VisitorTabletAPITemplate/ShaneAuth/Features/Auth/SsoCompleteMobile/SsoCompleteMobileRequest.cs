namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.SsoCompleteMobile
{
    public sealed class SsoCompleteMobileRequest
    {
        public string? Code { get; set; }
        public string? State { get; set; }
    }
}
