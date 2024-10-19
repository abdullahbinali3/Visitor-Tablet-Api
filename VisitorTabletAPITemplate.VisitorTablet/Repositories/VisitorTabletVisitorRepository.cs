using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.VisitorTablet.Repositories
{
    public sealed class VisitorTabletVisitorRepository
    {
        private readonly AppSettings _appSettings;

        public VisitorTabletVisitorRepository(AppSettings appSettings)
        {
            _appSettings = appSettings;
        }

        /// <summary>
        /// Handles cancelling or truncating a visit based on the current time.
        /// If cancelled before the visit starts, the visit is cancelled.
        /// If the visit is in progress, it is truncated (ends early).
        /// If the visit has already ended, it is marked as cancelled.
        /// </summary>
        /// <param name="visitId">The ID of the workplace visit.</param>
        /// <param name="currentTimeUtc">The current time in UTC.</param>
        /// <param name="currentTimeLocal">The current time in the local timezone.</param>
        /// <returns>Returns a SqlQueryResult indicating success or failure.</returns>
        public async Task<SqlQueryResult> CancelOrTruncateVisitAsync(Guid visitId, DateTime? currentTimeUtc, DateTime currentTimeLocal)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                await sqlConnection.OpenAsync();

                // Retrieve the start and end times of the visit
                var query = @"
                    SELECT [StartDateUtc], [EndDateUtc], [EndDateLocal]
                    FROM [dbo].[tblWorkplaceVisits]
                    WHERE [id] = @VisitId";

                var visit = await sqlConnection.QuerySingleOrDefaultAsync<dynamic>(query, new { VisitId = visitId });

                if (visit == null)
                {
                    return SqlQueryResult.RecordDidNotExist;
                }

                DateTime startDateUtc = visit.StartDateUtc;
                DateTime endDateUtc = visit.EndDateUtc;
                DateTime endDateLocal = visit.EndDateLocal;

                // Determine if visit is being cancelled before start, truncated, or cancelled after end
                if (currentTimeUtc < startDateUtc)
                {
                    // Cancel the visit before it starts
                    var cancelQuery = @"
                        UPDATE [dbo].[tblWorkplaceVisits]
                        SET [Cancelled] = 1, [CancelledDateUtc] = @CancelledDateUtc, [CancelledDateLocal] = @CancelledDateLocal
                        WHERE [id] = @VisitId";

                    await sqlConnection.ExecuteAsync(cancelQuery, new
                    {
                        CancelledDateUtc = currentTimeUtc,
                        CancelledDateLocal = currentTimeLocal,
                        VisitId = visitId
                    });
                }
                else if (currentTimeUtc >= startDateUtc && currentTimeUtc <= endDateUtc)
                {
                    // Truncate the visit (in progress)
                    var truncateQuery = @"
                        UPDATE [dbo].[tblWorkplaceVisits]
                        SET [Truncated] = 1, [EndDateUtc] = @EndDateUtc, [EndDateLocal] = @EndDateLocal,
                            [OriginalEndDateUtc] = @OriginalEndDateUtc, [OriginalEndDateLocal] = @OriginalEndDateLocal
                        WHERE [id] = @VisitId";

                    await sqlConnection.ExecuteAsync(truncateQuery, new
                    {
                        EndDateUtc = currentTimeUtc,
                        EndDateLocal = currentTimeLocal,
                        OriginalEndDateUtc = endDateUtc,
                        OriginalEndDateLocal = endDateLocal,
                        VisitId = visitId
                    });
                }
                else
                {
                    // Cancel the visit after it has ended
                    var cancelAfterQuery = @"
                        UPDATE [dbo].[tblWorkplaceVisits]
                        SET [Cancelled] = 1, [CancelledDateUtc] = @CancelledDateUtc, [CancelledDateLocal] = @CancelledDateLocal
                        WHERE [id] = @VisitId";

                    await sqlConnection.ExecuteAsync(cancelAfterQuery, new
                    {
                        CancelledDateUtc = currentTimeUtc,
                        CancelledDateLocal = currentTimeLocal,
                        VisitId = visitId
                    });
                }

                return SqlQueryResult.Ok;
            }
        }
    }
}
