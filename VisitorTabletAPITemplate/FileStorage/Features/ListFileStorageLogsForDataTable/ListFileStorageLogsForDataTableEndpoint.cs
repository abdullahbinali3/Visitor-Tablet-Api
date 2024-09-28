using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using VisitorTabletAPITemplate.FileStorage.Repositories;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.FileStorage.Features.ListFileStorageLogsForDataTable
{
    public sealed class ListFileStorageLogsForDataTableEndpoint : Endpoint<ListFileStorageLogsForDataTableRequest>
    {
        private readonly FileStorageRepository _fileStorageRepository;
        private readonly AuthCacheService _authCacheService;

        public ListFileStorageLogsForDataTableEndpoint(FileStorageRepository fileStorageRepository,
            AuthCacheService authCacheService)
        {
            _fileStorageRepository = fileStorageRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/logs/fileStorage/{organizationId}/listLogsForDataTable");
            SerializerContext(ListFileStorageLogsForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListFileStorageLogsForDataTableRequest req, CancellationToken ct)
        {
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
            DataTableResponse<FileStorageLog> fileStorageLogDataTable = await _fileStorageRepository.ListFileStorageLogsForDataTableAsync(req.OrganizationId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, SearchQueryParser.SplitTerms(req.Search), ct);

            await SendAsync(fileStorageLogDataTable);
        }

        private async Task ValidateInputAsync(ListFileStorageLogsForDataTableRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // If no page number specified just use page 1 as default
            if (!req.PageNumber.HasValue)
            {
                req.PageNumber = 1;
            }

            // If no page size specified just use 30 as default
            if (!req.PageSize.HasValue)
            {
                req.PageSize = 30;
            }

            // If no sort specified just use Created Date as default
            if (!req.Sort.HasValue || req.Sort == SortType.Unsorted)
            {
                req.Sort = SortType.Created;
            }
        }
    }
}
