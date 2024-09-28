namespace VisitorTabletAPITemplate.Features.Organizations.NotificationsSettings.UpdateOrganizationNotificationsSettings
{
    public sealed class UpdateOrganizationNotificationsSettingsRequest
    {
        public Guid? OrganizationId { get; set; }
        public bool? Enabled { get; set; }
        public bool? BookingModifiedByAdmin { get; set; }
        public bool? PermanentBookingAllocated { get; set; }
        public bool? CheckInReminderEnabled { get; set; }
        public bool? DeskBookingReminderEnabled { get; set; }
        public byte[]? ConcurrencyKey { get; set; } = default!;
    }
}
