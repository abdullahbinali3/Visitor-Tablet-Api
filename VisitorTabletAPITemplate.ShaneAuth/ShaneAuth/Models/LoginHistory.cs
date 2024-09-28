namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class LoginHistory
    {
        public Guid id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public Guid Uid { get; set; }
        public string? Source { get; set; }
        public bool Success { get; set; }
        public string? FailReason { get; set; }
        public string? LoginType { get; set; }
    }
}
