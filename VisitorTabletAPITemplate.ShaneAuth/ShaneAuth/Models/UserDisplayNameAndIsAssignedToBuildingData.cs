namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class UserDisplayNameAndIsAssignedToBuildingData
    {
        public Guid Uid { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string Email { get; set; } = default!;
        public string? AvatarUrl { get; set; }
        public string? AvatarThumbnailUrl { get; set; }

        public Guid OrganizationId { get; set; }
        public bool IsAssignedToBuilding { get; set; } // If true, then BuildingId and BuildingName are not null; Otherwise, they are null
        public Guid? BuildingId { get; set; }
    }
}
