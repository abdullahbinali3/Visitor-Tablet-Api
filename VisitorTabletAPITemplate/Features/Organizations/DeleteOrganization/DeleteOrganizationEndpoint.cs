using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Organizations.DeleteOrganization
{
    public sealed class DeleteOrganizationEndpoint : Endpoint<DeleteOrganizationRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;

        public DeleteOrganizationEndpoint(OrganizationsRepository organizationsRepository)
        {
            _organizationsRepository = organizationsRepository;
        }

        public override void Configure()
        {
            Post("/organizations/delete");
            SerializerContext(DeleteOrganizationContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(DeleteOrganizationRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Organization? organization) = await _organizationsRepository.DeleteOrganizationAsync(req, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, organization);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        private void ValidateInput(DeleteOrganizationRequest req)
        {
            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Organization Id is required.", "error.organization.idIsRequired");
            }

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, Organization? organization)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The organization was deleted since you last accessed this page.", "error.organization.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(organization!, DeleteOrganizationContext.Default.Organization));
                    AddError("The organization's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.organization.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
