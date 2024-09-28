using VisitorTabletAPITemplate.VisitorTablet.Enums;

namespace VisitorTabletAPITemplate.VisitorTablet.Models
{
    public sealed class Example
    {
        public Guid id { get; set; }
        public Guid OrganizationId { get; set; }
        public ExampleType ExampleType { get; set; }
    }
}
