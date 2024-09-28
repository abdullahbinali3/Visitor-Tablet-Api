namespace VisitorTabletAPITemplate.StartupValidation
{
    // https://andrewlock.net/adding-validation-to-strongly-typed-configuration-objects-in-asp-net-core/#creating-a-settings-validation-step-with-an-istartupfilter
    public sealed class SettingValidationStartupFilter : IStartupFilter
    {
        readonly IEnumerable<IValidatable> _validatableObjects;
        public SettingValidationStartupFilter(IEnumerable<IValidatable> validatableObjects)
        {
            _validatableObjects = validatableObjects;
        }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            foreach (IValidatable validatableObject in _validatableObjects)
            {
                validatableObject.Validate();
            }

            //don't alter the configuration
            return next;
        }
    }
}
