namespace VisitorTabletAPITemplate.ObjectClasses
{
    public sealed class ItemWithRequestCounter<T> where T : class, new()
    {
        public long? RequestCounter { get; set; }
        public T? Data { get; set; }
    }
}
