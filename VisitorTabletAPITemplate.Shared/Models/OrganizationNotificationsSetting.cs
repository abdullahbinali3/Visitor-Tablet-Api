namespace VisitorTabletAPITemplate.Models
{
    public sealed class OrganizationNotificationsSetting
    {
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = default!;
        public DateTime UpdatedDateUtc { get; set; }
        public bool Enabled { get; set; }
        public bool BookingModifiedByAdmin { get; set; }
        public bool PermanentBookingAllocated { get; set; }
        public bool CheckInReminderEnabled { get; set; }
        public bool DeskBookingReminderEnabled { get; set; }
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
