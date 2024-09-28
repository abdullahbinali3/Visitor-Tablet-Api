namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Disable2fa
{
    public sealed class AuthDisable2faRequest
    {
        public Guid? Uid { get; set; }
        public string? Token { get; set; }
    }
}
