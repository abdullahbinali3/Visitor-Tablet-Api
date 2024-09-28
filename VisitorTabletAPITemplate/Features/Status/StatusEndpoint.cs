namespace VisitorTabletAPITemplate.Features.Status
{
    public sealed class StatusEndpoint : EndpointWithoutRequest
    {
        private static readonly string statusString = $"{typeof(StatusEndpoint).Namespace!.Split(".")[0]} is running.";

        public override void Configure()
        {
            Get("/status");
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            await SendAsync(statusString);
        }
    }
}
