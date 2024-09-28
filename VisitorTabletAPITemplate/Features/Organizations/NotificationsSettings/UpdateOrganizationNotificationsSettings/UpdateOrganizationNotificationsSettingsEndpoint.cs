using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Organizations.NotificationsSettings.UpdateOrganizationNotificationsSettings
{
    public sealed class UpdateOrganizationNotificationsSettingsEndpoint : Endpoint<UpdateOrganizationNotificationsSettingsRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public UpdateOrganizationNotificationsSettingsEndpoint(OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/organizations/notificationsSettings/update");
            SerializerContext(UpdateOrganizationNotificationsSettingsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateOrganizationNotificationsSettingsRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, OrganizationNotificationsSetting? organizationNotificationsSetting) = await _organizationsRepository.UpdateOrganizationNotificationsSettingsAsync(req, userId!.Value, adminUserDisplayName, remoteIpAddress);

            // If no settings in database, just return empty object
            if (organizationNotificationsSetting == null)
            {
                organizationNotificationsSetting = new OrganizationNotificationsSetting();
            }

            // Validate result
            ValidateOutput(queryResult, organizationNotificationsSetting);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(organizationNotificationsSetting);
        }

        private async Task ValidateInputAsync(UpdateOrganizationNotificationsSettingsRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Validate input

            // Validate NotificationsEnabled
            if (!req.Enabled.HasValue)
            {
                AddError(m => m.Enabled!, "Notifications Enabled is required.", "error.organizationSettings.notifications.enabledIsRequired");
            }

            // Validate BookingModifiedByAdmin
            if (!req.BookingModifiedByAdmin.HasValue)
            {
                AddError(m => m.BookingModifiedByAdmin!, "Booking Modified By Admin is required.", "error.organizationSettings.notifications.bookingModifiedByAdminIsRequired");
            }

            // Validate PermanentBookingAllocated
            if (!req.PermanentBookingAllocated.HasValue)
            {
                AddError(m => m.PermanentBookingAllocated!, "Permanent Booking Allocated is required.", "error.organizationSettings.notifications.permanentBookingAllocatedIsRequired");
            }

            // Validate CheckInReminderEnabled
            if (!req.CheckInReminderEnabled.HasValue)
            {
                AddError(m => m.CheckInReminderEnabled!, "Check-In Reminder Enabled is required.", "error.organizationSettings.notifications.checkInReminderEnabledIsRequired");
            }

            // Validate DeskBookingReminderEnabled
            if (!req.DeskBookingReminderEnabled.HasValue)
            {
                AddError(m => m.DeskBookingReminderEnabled!, "Desk Booking Reminder Enabled is required.", "error.organizationSettings.notifications.deskBookingReminderEnabledIsRequired");
            }

            // Validate ConcurrencyKey
            // Only validating this if present, for the case where no settings are present in the database yet.
            if (req.ConcurrencyKey is not null && req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, OrganizationNotificationsSetting? organizationNotificationsSetting)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(organizationNotificationsSetting!, UpdateOrganizationNotificationsSettingsContext.Default.OrganizationNotificationsSetting));
                    AddError("The organization notifications settings have changed since you last accessed this page. Please review the current updated version of the settings below, then submit your changes again if you wish to overwrite.", "error.organizationSettings.notifications.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
