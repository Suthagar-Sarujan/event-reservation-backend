using EventReservation.Application.DTOs;

namespace EventReservation.Application.Services;

public interface IEventService
{
    Task<(int Total, int Page, int PageSize, List<EventSummaryDto> Items)> SearchAsync(
        string? search, string? type, string? taxonomySubName, bool bookableOnly, int page, int pageSize);

    Task<(List<string> Types, List<string> SubCategories)> GetFiltersAsync();

    Task<EventDetailDto?> GetDetailAsync(long id);

    Task<List<RecommendedEventDto>?> GetSimilarAsync(long id, int topN);
}
