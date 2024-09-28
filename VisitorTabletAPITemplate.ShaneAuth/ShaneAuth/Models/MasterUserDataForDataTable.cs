using VisitorTabletAPITemplate.ShaneAuth.Enums;

namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class MasterUserDataForDataTable
    {
        public Guid Uid { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime? LastAccessDateUtc { get; set; }
        public string Email { get; set; } = default!;
        public UserSystemRole? UserSystemRole { get; set; }
        public string? DisplayName { get; set; }
        public string? AvatarUrl { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
        public bool Disabled { get; set; }
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
