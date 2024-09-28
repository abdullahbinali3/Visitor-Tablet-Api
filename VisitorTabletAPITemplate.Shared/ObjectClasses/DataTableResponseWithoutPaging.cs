namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class DataTableResponseWithoutPaging<T> where T : class, new()
    {
        public long? RequestCounter { get; set; }
        public List<T>? Records { get; set; }
    }
}
