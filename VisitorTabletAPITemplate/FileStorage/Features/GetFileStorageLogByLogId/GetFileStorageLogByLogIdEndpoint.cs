using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using VisitorTabletAPITemplate.FileStorage.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.FileStorage.Features.GetFileStorageLogByLogId
{
    public sealed class GetFileStorageLogByLogIdEndpoint : Endpoint<GetFileStorageLogByLogIdRequest>
    {
        private readonly FileStorageRepository _fileStorageRepository;
        private readonly AuthCacheService _authCacheService;

        public GetFileStorageLogByLogIdEndpoint(FileStorageRepository fileStorageRepository,
            AuthCacheService authCacheService)
        {
            _fileStorageRepository = fileStorageRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/logs/fileStorage/{organizationId}/getLogByLogId/{logId}");
            SerializerContext(GetFileStorageLogByLogIdContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetFileStorageLogByLogIdRequest req, CancellationToken ct)
        {
            // Get logged in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            FileStorageLog? fileStorageLog = await _fileStorageRepository.GetFileStorageLogByLogIdAsync(req.LogId!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(fileStorageLog);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(fileStorageLog!);
        }

        private async Task ValidateInputAsync(GetFileStorageLogByLogIdRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate LogId
            if (!req.LogId.HasValue)
            {
                AddError(m => m.LogId!, "Log Id is required.", "error.logIdIsRequired");
            }
        }

        private void ValidateOutput(FileStorageLog? fileStorageLog)
        {
            if (fileStorageLog is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError(m => m.LogId!, "The selected file storage log did not exist.", "error.fileStorage.logDidNotExist");
            }
        }
    }
}
