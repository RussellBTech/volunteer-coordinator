namespace VolunteerCoordinator.Application.Models;

public sealed record CoordinatorMessagePageDto(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CoordinatorMessageDto> Messages)
{
    public int PageCount => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < PageCount;
}
