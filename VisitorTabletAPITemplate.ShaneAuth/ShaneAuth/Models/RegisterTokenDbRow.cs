namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class RegisterTokenDbRow
    {
        public string Email { get; set; } = default!;
        public string RegisterToken { get; set; } = default!;
        public DateTime InsertDateUtc { get; set; }
        public DateTime ExpiryDateUtc { get; set; }
        public string? Location { get; set; }
        public string? BrowserName { get; set; }
        public string? OSName { get; set; }
        public string? DeviceInfo { get; set; }
    }
}
