namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class RegisterFormData
    {
        public string Email { get; set; } = default!;
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public Guid OrganizationId { get; set; }
        public string? AvatarUrl { get; set; }
        public Guid? AvatarImageStorageId { get; set; }
        public List<RegisterFormData_Region> Regions { get; set; } = new List<RegisterFormData_Region>();
    }

    public sealed class RegisterFormData_Region
    {
        public Guid RegionId { get; set; }
        public string RegionName { get; set; } = default!;
        public List<RegisterFormData_Building> Buildings { get; set; } = new List<RegisterFormData_Building>();
    }

    public sealed class RegisterFormData_Building
    {
        public Guid RegionId { get; set; }
        public Guid BuildingId { get; set; }
        public string BuildingName { get; set; } = default!;
        public List<RegisterFormData_Function> Functions { get; set; } = new List<RegisterFormData_Function>();
    }

    public sealed class RegisterFormData_Function
    {
        public Guid BuildingId { get; set; }
        public Guid FunctionId { get; set; }
        public string FunctionName { get; set; } = default!;
    }
}
