namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class ContentInspectorResultWithMemoryStream
    {
        public MemoryStream? FileDataStream { get; set; }
        public string? FileName { get; set; }
        public string? OriginalExtension { get; set; }
        public string? InspectedExtension { get; set; }
        public string? InspectedMimeType { get; set; }
        public bool IsSanitized { get; set; }
    }
}
