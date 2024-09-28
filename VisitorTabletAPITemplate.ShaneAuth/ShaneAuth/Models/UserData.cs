using System.Text.Json.Serialization;
using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class UserData
    {
        public Guid Uid { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public DateTime? LastAccessDateUtc { get; set; }
        public DateTime? LastPasswordChangeDateUtc { get; set; }
        public string Email { get; set; } = default!;
        public bool HasPassword { get; set; }
        /// <summary>
        /// <see cref="PasswordHash"/> is for internal password verification use and will always be returned as null.
        /// </summary>
        [JsonIgnore]
        public string? PasswordHash { get; set; }
        [JsonIgnore]
        public DateTime? PasswordLockoutEndDateUtc { get; set; }
        public bool TotpEnabled { get; set; }
        /// <summary>
        /// <see cref="TotpSecret"/> is for internal 2fa verification use and will always be returned as null.
        /// </summary>
        [JsonIgnore]
        public string? TotpSecret { get; set; }
        [JsonIgnore]
        public DateTime? TotpLockoutEndDateUtc { get; set; }
        public UserSystemRole? UserSystemRole { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string? Timezone { get; set; }
        public string? AvatarUrl { get; set; }
        public Guid? AvatarImageStorageId { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
        public Guid? AvatarThumbnailStorageId { get; set; }
        public bool Disabled { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
        public UserData_ExtendedData ExtendedData { get; set; } = new UserData_ExtendedData();
    }

    public sealed class UserData_ExtendedData
    {
        public List<UserData_UserOrganizations>? Organizations { get; set; }
        public UserData_LastUsedBuilding? LastUsedBuilding { get; set; }
        public UserData_MasterInfo? MasterInfo { get; set; }
    }

    public sealed class UserData_UserOrganizations
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public string? LogoImageUrl { get; set; }
        public bool CheckInEnabled { get; set; }
        public bool WorkplacePortalEnabled { get; set; }
        public bool WorkplaceAccessRequestsEnabled { get; set; }
        public bool WorkplaceInductionsEnabled { get; set; }
        public int UserOrganizationRole { get; set; }
        public string? Note { get; set; }
        public bool Contractor { get; set; }
        public bool Visitor { get; set; }
        public bool UserOrganizationDisabled { get; set; }
        public DateTime AccessGivenDateUtc { get; set; }
        public List<UserData_Building> Buildings { get; set; } = new List<UserData_Building>();
    }

    public sealed class UserData_Building
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = default!;
        public Guid OrganizationId { get; set; }
        public string Timezone { get; set; } = default!;
        public bool CheckInEnabled { get; set; }
        public bool HasBookableDesks { get; set; }
        public bool HasBookableMeetingRooms { get; set; }
        public bool HasBookableAssetSlots { get; set; }
        public Guid FunctionId { get; set; }
        public string FunctionName { get; set; } = default!;
        public string FunctionHtmlColor { get; set; } = default!;
        public bool FirstAidOfficer { get; set; }
        public bool FireWarden { get; set; }
        public bool PeerSupportOfficer { get; set; }
        public bool AllowBookingDeskForVisitor { get; set; }
        public bool AllowBookingRestrictedRooms { get; set; }
        public bool AllowBookingAnyoneAnywhere { get; set; }
        public Guid? PermanentDeskId { get; set; }
        public DateTime AccessGivenDateUtc { get; set; }
        public List<UserData_AssetType> AssetTypes { get; set; } = new List<UserData_AssetType>();
        public List<UserData_AdminFunction> AdminFunctions { get; set; } = new List<UserData_AdminFunction>();
        public List<UserData_AdminAssetType> AdminAssetTypes { get; set; } = new List<UserData_AdminAssetType>();
    }

    public sealed class UserData_PermanentSeat
    {
        public Guid DeskId { get; set; }
        public Guid BuildingId { get; set; }
    }

    public sealed class UserData_AssetType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
        public string? LogoImageUrl { get; set; }
        public Guid? PermanentAssetSlotId { get; set; }
    }

    public sealed class UserData_AdminFunction
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
    }

    public sealed class UserData_AdminAssetType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
        public string? LogoImageUrl { get; set; }
    }

    public sealed class UserData_PermanentAsset
    {
        public Guid AssetSlotId { get; set; }
        public Guid AssetTypeId { get; set; }
        public Guid BuildingId { get; set; }
    }

    public sealed class UserData_MasterInfo
    {
        public bool AnyOrganizationsExist { get; set; }
    }

    public sealed class UserData_LastUsedBuilding
    { 
        public Guid Uid { get; set; }
        public Guid? WebLastUsedOrganizationId { get; set; }
        public Guid? WebLastUsedBuildingId { get; set; }
        public Guid? MobileLastUsedOrganizationId { get; set; }
        public Guid? MobileLastUsedBuildingId { get; set; }
    }
}
