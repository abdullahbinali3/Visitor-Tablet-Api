namespace VisitorTabletAPITemplate.Features.VisitTracking.CheckInSystem
{
    public sealed class CheckInSystemRequest
    {
        public Guid Id { get; set; }
        public Guid? HostId { get; set; }
        public Guid? VisitorId { get; set; }
        public DateTime CheckinTime { get; set; }
        public DateTime? CheckoutTime { get; set; }
        public string CheckinSource { get; set; }
        public string CheckoutSource { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public bool Disabled { get; set; } = false;
        public bool Deleted { get; set; } = false;
    }
}
