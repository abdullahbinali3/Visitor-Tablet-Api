using VisitorTabletAPITemplate.ObjectClasses;

namespace VisitorTabletAPITemplate.ShaneAuth.ShaneAuth.Models
{
    public sealed class MicrosoftUserProfilePhotoData
    {
        public ContentInspectorResultWithMemoryStream? ProfilePhoto { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? ObjectId { get; set; }
    }
}
