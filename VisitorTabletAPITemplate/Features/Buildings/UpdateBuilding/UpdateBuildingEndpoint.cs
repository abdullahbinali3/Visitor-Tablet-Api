using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Buildings.UpdateBuilding
{
    public sealed class UpdateBuildingEndpoint : Endpoint<UpdateBuildingRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly BuildingsRepository _buildingsRepository;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public UpdateBuildingEndpoint(AppSettings appSettings,
            BuildingsRepository buildingsRepository,
            OrganizationsRepository organizationsRepository,
            RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _buildingsRepository = buildingsRepository;
            _organizationsRepository = organizationsRepository;
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/buildings/{organizationId}/update");
            SerializerContext(UpdateBuildingContext.Default);
            Policies("User");
            AllowFileUploads();
        }

        public override async Task HandleAsync(UpdateBuildingRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ContentInspectorResultWithMemoryStream? featureImageContentInspectorResult = await ValidateInputAsync(req, userId.Value, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Building? building) = await _buildingsRepository.UpdateBuildingAsync(req, featureImageContentInspectorResult, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, building);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(building!);
        }

        private async Task<ContentInspectorResultWithMemoryStream?> ValidateInputAsync(UpdateBuildingRequest req, Guid userId, CancellationToken ct)
        {
            ContentInspectorResultWithMemoryStream? featureImageContentInspectorResult = null;

            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return featureImageContentInspectorResult;
            }

            // Get organization params to check max capacity enabled for the organization
            Organization? organization = await _organizationsRepository.GetOrganizationAsync(req.OrganizationId!.Value);

            if (organization is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("You do not have permission to perform this action.", "error.doNotHavePermission");
                return featureImageContentInspectorResult;
            }

            // Trim strings
            req.Name = req.Name?.Trim();
            req.Address = req.Address?.Trim();
            req.Timezone = req.Timezone?.Trim();
            req.FacilitiesManagementEmail = req.FacilitiesManagementEmail?.Trim();
            req.FacilitiesManagementEmailDisplayName = req.FacilitiesManagementEmailDisplayName?.Trim();

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Building Id is required.", "error.building.idIsRequired");
            }

            // Validate RegionId
            if (!req.RegionId.HasValue)
            {
                AddError(m => m.RegionId!, "Region is required.", "error.building.regionIsRequired");
            }
            else if (!await _regionsRepository.IsRegionExistsAsync(req.RegionId.Value, req.OrganizationId!.Value))
            {
                AddError(m => m.RegionId!, "Region is invalid.", "error.building.regionIsInvalid");
            }

            // Validate Name
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                AddError(m => m.Name!, "Building Name is required.", "error.building.nameIsRequired");
            }
            else if (req.Name.Length > 100)
            {
                AddError(m => m.Name!, "Building Name must be 100 characters or less.", "error.building.nameLength|{\"length\":\"100\"}");
            }

            // Validate Address
            if (string.IsNullOrWhiteSpace(req.Address))
            {
                AddError(m => m.Address!, "Address is required.", "error.building.addressIsRequired");
            }
            else if (req.Address.Length > 250)
            {
                AddError(m => m.Address!, "Address must be 250 characters or less.", "error.building.addressLength|{\"length\":\"250\"}");
            }

            // Validate Latitude
            if (!req.Latitude.HasValue)
            {
                AddError(m => m.Latitude!, "Latitude is required.", "error.building.latitudeIsRequired");
            }
            else if (!Toolbox.IsValidLatitude((float)req.Latitude.Value))
            {
                AddError(m => m.Latitude!, "Latitude must be between -90 and 90.", "error.building.latitudeIsInvalid");
            }

            // Validate Longitude
            if (!req.Longitude.HasValue)
            {
                AddError(m => m.Longitude!, "Longitude is required.", "error.building.longitudeIsRequired");
            }
            else if (!Toolbox.IsValidLongtitude((float)req.Longitude.Value))
            {
                AddError(m => m.Longitude!, "Longitude must be between -180 and 180.", "error.building.longitudeIsInvalid");
            }

            // Validate Timezone
            if (string.IsNullOrWhiteSpace(req.Timezone))
            {
                AddError(m => m.Timezone!, "Timezone is required.", "error.building.timezoneIsRequired");
            }
            else
            {
                if (req.Timezone.Length > 50)
                {
                    AddError(m => m.Timezone!, "Timezone must be 50 characters or less.", "error.building.timezoneLength|{\"length\":\"50\"}");
                }
                else if (!Toolbox.IsValidTimezone(req.Timezone))
                {
                    AddError(m => m.Timezone!, "The specified timezone is not a valid timezone.", "error.building.timezoneIsInvalid");
                }
            }

            // Validate FacilitiesManagementEmail
            if (string.IsNullOrWhiteSpace(req.FacilitiesManagementEmail))
            {
                AddError(m => m.FacilitiesManagementEmail!, "Facilities Management Email is required.", "error.building.facilitiesManagementEmailIsRequired");
            }
            else
            {
                if (req.FacilitiesManagementEmail.Length > 254)
                {
                    AddError(m => m.FacilitiesManagementEmail!, "Facilities Management Email must be 254 characters or less.", "error.building.facilitiesManagementEmailLength|{\"length\":\"254\"}");
                }
                else if (!Toolbox.IsValidEmail(req.FacilitiesManagementEmail))
                {
                    AddError(m => m.FacilitiesManagementEmail!, "Facilities Management Email format is invalid.", "error.building.facilitiesManagementEmailIsInvalid");
                }
            }

            // Validate FacilitiesManagementEmailDisplayName
            if (string.IsNullOrWhiteSpace(req.FacilitiesManagementEmailDisplayName))
            {
                AddError(m => m.FacilitiesManagementEmailDisplayName!, "Facilities Management Email Display Name is required.", "error.building.facilitiesManagementEmailDisplayNameIsRequired");
            }
            else
            {
                if (req.FacilitiesManagementEmailDisplayName.Length > 151)
                {
                    AddError(m => m.FacilitiesManagementEmailDisplayName!, "Facilities Management Email Display Name must be 151 characters or less.", "error.building.facilitiesManagementEmailDisplayNameLength|{\"length\":\"151\"}");
                }
            }

            // Validate FeatureImage
            if (!req.FeatureImageChanged.HasValue)
            {
                AddError(m => m.FeatureImageChanged!, "Feature Image Changed is required.", "error.building.featureImageChangedIsRequired");
            }
            else if (req.FeatureImageChanged.Value && req.FeatureImage is not null)
            {
                if (req.FeatureImage.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.FeatureImage!, $"Building Feature Image maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.building.featureImageMaximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    featureImageContentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.FeatureImage);

                    if (featureImageContentInspectorResult is null
                        || featureImageContentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidImageExtension(featureImageContentInspectorResult.InspectedExtension))
                    {
                        AddError(m => m.FeatureImage!, $"Building Feature Image should be one of the following formats: {ImageStorageHelpers.ValidImageFormats}",
                            "error.building.featureImageInvalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidImageFormats + "\"}");
                    }
                }
            }

            // Validate CheckInEnabled
            if (!req.CheckInEnabled.HasValue)
            {
                AddError(m => m.CheckInEnabled!, "Check-In Enabled is required.", "error.building.checkInEnabledIsRequired");
            }
            else if (req.CheckInEnabled.Value)
            {
                // Validate AccessCardCheckInWithBookingMessage
                if (string.IsNullOrWhiteSpace(req.AccessCardCheckInWithBookingMessage))
                {
                    AddError(m => m.AccessCardCheckInWithBookingMessage!, "Check-In Message via Access Card with Existing Booking is required.", "error.building.accessCardCheckInWithBookingMessageIsRequired");
                }
                else if (req.AccessCardCheckInWithBookingMessage.Length > 2000)
                {
                    AddError(m => m.AccessCardCheckInWithBookingMessage!, "Check-In Message via Access Card with Existing Booking must be 2000 characters or less.", "error.building.accessCardCheckInWithBookingMessageLength|{\"length\":\"2000\"}");
                }

                // Validate AccessCardCheckInWithoutBookingMessage
                if (string.IsNullOrWhiteSpace(req.AccessCardCheckInWithoutBookingMessage))
                {
                    AddError(m => m.AccessCardCheckInWithoutBookingMessage!, "Check-In Message via Access Card without Existing Booking is required.", "error.building.accessCardCheckInWithoutBookingMessageIsRequired");
                }
                else if (req.AccessCardCheckInWithoutBookingMessage.Length > 2000)
                {
                    AddError(m => m.AccessCardCheckInWithoutBookingMessage!, "Check-In Message via Access Card without Existing Booking must be 2000 characters or less.", "error.building.accessCardCheckInWithoutBookingMessageLength|{\"length\":\"2000\"}");
                }

                // Clear CheckInQRCode if not specified
                if (string.IsNullOrWhiteSpace(req.CheckInQRCode))
                {
                    req.CheckInQRCode = null;
                }

                // Validate QRCodeCheckInWithBookingMessage
                if (string.IsNullOrWhiteSpace(req.QRCodeCheckInWithBookingMessage))
                {
                    AddError(m => m.QRCodeCheckInWithBookingMessage!, "Check-In Message via QR Code with Existing Booking is required.", "error.building.qrCodeCheckInWithBookingMessageIsRequired");
                }
                else if (req.QRCodeCheckInWithBookingMessage.Length > 2000)
                {
                    AddError(m => m.QRCodeCheckInWithBookingMessage!, "Check-In Message via QR Code with Existing Booking must be 2000 characters or less.", "error.building.qrCodeCheckInWithBookingMessageLength|{\"length\":\"2000\"}");
                }

                // Validate QRCodeCheckInWithoutBookingMessage
                if (string.IsNullOrWhiteSpace(req.QRCodeCheckInWithoutBookingMessage))
                {
                    AddError(m => m.QRCodeCheckInWithoutBookingMessage!, "Check-In Message via QR Code without Existing Booking is required.", "error.building.qrCodeCheckInWithoutBookingMessageIsRequired");
                }
                else if (req.QRCodeCheckInWithoutBookingMessage.Length > 2000)
                {
                    AddError(m => m.QRCodeCheckInWithoutBookingMessage!, "Check-In Message via QR Code without Existing Booking must be 2000 characters or less.", "error.building.qrCodeCheckInWithoutBookingMessageLength|{\"length\":\"2000\"}");
                }
            }

            // Validate CheckInReminderEnabled
            if (!req.CheckInReminderEnabled.HasValue)
            {
                AddError(m => m.CheckInReminderEnabled!, "Check-In Reminder Enabled is required.", "error.building.checkInReminderEnabledIsRequired");
            }
            else if (req.CheckInReminderEnabled.Value)
            {
                // Validate CheckInReminderTime
                if (!req.CheckInReminderTime.HasValue)
                {
                    AddError(m => m.CheckInReminderTime!, "Check-In Reminder Time is required.", "error.building.checkInReminderTimeIsRequired");
                }
                else 
                {
                    var minute = req.CheckInReminderTime.Value.Minute;

                    if (minute != 0 && minute != 15 && minute != 30 && minute != 45) 
                    { 
                    AddError(m => m.CheckInReminderTime!, "Check-In Reminder Time is invalid.", "error.building.checkInReminderTimeIsInvalid");
                    }
                }

                // Validate CheckInReminderMessage
                if (string.IsNullOrWhiteSpace(req.CheckInReminderMessage))
                {
                    AddError(m => m.CheckInReminderMessage!, "Check-In Reminder Message is required.", "error.building.checkInReminderMessageIsRequired");
                }
                else if (req.CheckInReminderMessage.Length > 2000)
                {
                    AddError(m => m.CheckInReminderMessage!, "Check-In Reminder Message must be 2000 characters or less.", "error.building.checkInReminderMessage|{\"length\":\"2000\"}");
                }
            }

            // Validate CheckInQRCode
            if (!string.IsNullOrWhiteSpace(req.CheckInQRCode) && req.CheckInQRCode.Length > 100)
            {
                AddError(m => m.CheckInQRCode!, "Check-In QR Code must be 100 characters or less.", "error.building.checkInQRCodeLength|{\"length\":\"100\"}");
            }

            // Check if Organization has automatic user inactivity enabled
            if (!organization.AutomaticUserInactivityEnabled)
            {
                req.AutoUserInactivityEnabled = false;
                req.AutoUserInactivityDurationDays = null;
                req.AutoUserInactivityScheduledIntervalMonths = null;
                req.AutoUserInactivityScheduleStartDateUtc = null;
            }
            else
            {
                // Validate AutoUserInactivityEnabled
                if (!req.AutoUserInactivityEnabled.HasValue)
                {
                    AddError(m => m.AutoUserInactivityEnabled!, "Automatic User Inactivity Enabled is required.", "error.building.autoUserInactivityEnabledIsRequired");
                }
                else if (req.AutoUserInactivityEnabled.Value)
                {
                    // Validate AutoUserInactivityDurationDays
                    if (!req.AutoUserInactivityDurationDays.HasValue)
                    {
                        AddError(m => m.AutoUserInactivityDurationDays!, "Automatic User Inactivity Duration Days is required.", "error.building.autoUserInactivityDurationDaysIsRequired");
                    }
                    else if (req.AutoUserInactivityDurationDays.Value <= 0)
                    {
                        AddError(m => m.AutoUserInactivityDurationDays!, "Automatic User Inactivity Duration Days must be a positive integer.", "error.building.autoUserInactivityDurationDaysMustBePositiveInteger");
                    }

                    // Validate AutoUserInactivityScheduledIntervalMonths
                    if (!req.AutoUserInactivityScheduledIntervalMonths.HasValue)
                    {
                        AddError(m => m.AutoUserInactivityScheduledIntervalMonths!, "Automatic User Inactivity Scheduled Interval Months is required.", "error.building.autoUserInactivityScheduledIntervalMonthsIsRequired");
                    }
                    else if (req.AutoUserInactivityScheduledIntervalMonths.Value <= 0 || req.AutoUserInactivityScheduledIntervalMonths > 12)
                    {
                        AddError(m => m.AutoUserInactivityScheduledIntervalMonths!, "Automatic User Inactivity Scheduled Interval Months must between 1 and 12.", "error.building.autoUserInactivityScheduledIntervalMonthsMustBePositiveInteger");
                    }

                    // Validate AutoUserInactivityScheduleStartDateUtc
                    if (!req.AutoUserInactivityScheduleStartDateUtc.HasValue)
                    {
                        AddError(m => m.AutoUserInactivityScheduleStartDateUtc!, "Automatic User Inactivity Schedule Start Date is required.", "error.building.autoUserInactivityScheduleStartDateUtcIsRequired");
                    }
                    else
                    {
                        var scheduleStartDateUtc = req.AutoUserInactivityScheduleStartDateUtc.Value;
                        if (scheduleStartDateUtc.Day > 28 || scheduleStartDateUtc.Day < 1)
                        {
                            AddError(m => m.AutoUserInactivityScheduleStartDateUtc!, "Automatic User Inactivity Schedule Start Date's day must be between 1st and 28th.", "error.building.autoUserInactivityScheduleStartDateUtcDayIsInvalid");
                        }
                    }
                }
            }

            // Validate MaxCapacity
            if (organization.MaxCapacityEnabled)
            {
                if (!req.MaxCapacityEnabled.HasValue)
                {
                    AddError(m => m.MaxCapacityEnabled!, "Max Capacity Enabled is required.", "error.building.maxCapacityEnabledIsRequired");
                }
                else if (req.MaxCapacityEnabled.Value)
                {
                    // Validate MaxCapacityUsers and check int
                    if (!req.MaxCapacityUsers.HasValue)
                    {
                        AddError(m => m.MaxCapacityUsers!, "Max Capacity Users is required.", "error.building.maxCapacityIsRequired");
                    }
                    else if (req.MaxCapacityUsers.Value <= 0)
                    {
                        AddError(m => m.MaxCapacityUsers!, "Max Capacity Users must be a positive integer.", "error.building.maxCapacityMustBePositiveInteger");
                    }

                    // Validate MaxCapacityNotificationMessage
                    if (string.IsNullOrWhiteSpace(req.MaxCapacityNotificationMessage))
                    {
                        AddError(m => m.MaxCapacityNotificationMessage!, "Max Capacity Message is required.", "error.building.maxCapacityNotificationMessageIsRequired");
                    }
                    else if (req.MaxCapacityNotificationMessage.Length > 2000)
                    {
                        AddError(m => m.MaxCapacityNotificationMessage!, "Max Capacity Message must be {length} characters or less.", "error.building.maxCapacityNotificationMessageLength|{\"length\":\"2000\"}");
                    }
                }
            }
            else
            {
                // If the organization has Max Capacity disabled, then we also disable it for the building
                req.MaxCapacityEnabled = false;
            }

            // Validate DeskBookingReminderEnabled
            if (!req.DeskBookingReminderEnabled.HasValue)
            {
                AddError(m => m.DeskBookingReminderEnabled!, "Desk Booking Reminder Enabled is required.", "error.building.deskBookingReminderEnabledIsRequired");
            }
            else if (req.DeskBookingReminderEnabled.Value)
            {
                // Validate DeskBookingReminderTime
                if (!req.DeskBookingReminderTime.HasValue)
                {
                    AddError(m => m.DeskBookingReminderTime!, "Desk Booking Reminder Time is required.", "error.building.deskBookingReminderTimeIsRequired");
                }
                else
                {
                    var minute = req.DeskBookingReminderTime.Value.Minute;

                    if (minute != 0 && minute != 15 && minute != 30 && minute != 45)
                    {
                        AddError(m => m.DeskBookingReminderTime!, "Desk Booking Reminder Time is invalid.", "error.building.deskBookingReminderTimeIsInvalid");
                    }
                }

                // Validate DeskBookingReminderMessage
                if (string.IsNullOrWhiteSpace(req.DeskBookingReminderMessage))
                {
                    AddError(m => m.DeskBookingReminderMessage!, "Desk Booking Reminder Message is required.", "error.building.deskBookingReminderMessageIsRequired");
                }
                else if (req.DeskBookingReminderMessage.Length > 2000)
                {
                    AddError(m => m.DeskBookingReminderMessage!, "Desk Booking Reminder Message must be 2000 characters or less.", "error.building.deskBookingReminderMessage|{\"length\":\"2000\"}");
                }
            }

            // Validate DeskBookingReservationDateRange
            if (!req.DeskBookingReservationDateRangeEnabled.HasValue)
            {
                AddError(m => m.DeskBookingReservationDateRangeEnabled!, "Desk booking reservation date enabled is required.", "error.building.deskBookingReservationDateEnabledIsRequired");
            }
            else if (req.DeskBookingReservationDateRangeEnabled.Value)
            {
                // Validate DeskBookingReservationDateRangeForUser
                if (!req.DeskBookingReservationDateRangeForUser.HasValue)
                {
                    AddError(m => m.DeskBookingReservationDateRangeForUser!, "Desk booking reservation date range for User is required.", "error.building.deskBookingReservationDateForUserIsRequired");
                }
                else if (req.DeskBookingReservationDateRangeForUser.Value <= 0)
                {
                    AddError(m => m.DeskBookingReservationDateRangeForUser!, "Desk booking reservation date range for User must be a positive integer.", "error.building.deskBookingReservationDateForUserMustBePositiveInteger");
                }

                // Validate DeskAssetBookingReservationDateRangeForAdmin
                if (!req.DeskBookingReservationDateRangeForAdmin.HasValue)
                {
                    AddError(m => m.DeskBookingReservationDateRangeForAdmin!, "Desk booking reservation date range for Admin is required.", "error.building.deskBookingReservationDateForAdminIsRequired");
                }
                else if (req.DeskBookingReservationDateRangeForAdmin.Value <= 0)
                {
                    AddError(m => m.DeskBookingReservationDateRangeForAdmin!, "Desk booking reservation date range for Admin must be a positive integer.", "error.building.deskBookingReservationDateForAdminMustBePositiveInteger");
                }

                // Validate DeskAssetBookingReservationDateRangeForSuperAdmin
                if (!req.DeskBookingReservationDateRangeForSuperAdmin.HasValue)
                {
                    AddError(m => m.DeskBookingReservationDateRangeForSuperAdmin!, "Desk booking reservation date range for Super Admin is required.", "error.building.deskBookingReservationDateForAdminIsRequired");
                }
                else if (req.DeskBookingReservationDateRangeForSuperAdmin.Value <= 0)
                {
                    AddError(m => m.DeskBookingReservationDateRangeForSuperAdmin!, "Desk booking reservation date range for Super Admin must be a positive integer.", "error.building.deskBookingReservationDateForAdminMustBePositiveInteger");
                }
            }

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }

            return featureImageContentInspectorResult;
        }

        private void ValidateOutput(SqlQueryResult queryResult, Building? building)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    if (building is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The building was deleted since you last accessed this page.", "error.building.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another building already exists with the specified name.", "error.building.nameExists");
                    break;
                case SqlQueryResult.SubRecordAlreadyExists:
                    AddError(m => m.CheckInQRCode!, "The specified Check-In QR Code has already been assigned to another building.", "error.building.checkInQRCodeExists");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(building!, UpdateBuildingContext.Default.Building));
                    AddError("The building's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.building.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
