namespace VisitorTabletAPITemplate.StartupValidation
{
    // https://andrewlock.net/adding-validation-to-strongly-typed-configuration-objects-in-asp-net-core/#creating-a-settings-validation-step-with-an-istartupfilter
    public interface IValidatable
    {
        void Validate();
    }
}
