using VolunteerCoordinator.Domain;
using VolunteerCoordinator.Domain.Schedules;
using Xunit;

namespace VolunteerCoordinator.UnitTests.Schedules;

public sealed class ShiftTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_RejectsInvalidInterval()
    {
        var exception = Assert.Throws<DomainException>(() =>
            Shift.Create("Service", null, null, Now.AddHours(2), Now.AddHours(1), 0));

        Assert.Contains("end", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_BuildsOnePrimaryAndConfiguredBackups()
    {
        var shift = Shift.Create("Service", "Hall", null, Now.AddHours(1), Now.AddHours(2), 2);

        Assert.Equal(3, shift.Slots.Count);
        Assert.Single(shift.Slots, x => x.Kind == SlotKind.Primary && x.Position == 1);
        Assert.Equal(2, shift.Slots.Count(x => x.Kind == SlotKind.Backup));
    }

    [Fact]
    public void Publish_RequiresActiveFutureShift()
    {
        var past = Shift.Create("Past", null, null, Now.AddHours(-2), Now.AddHours(-1), 0);
        var inactive = Shift.Create("Inactive", null, null, Now.AddHours(1), Now.AddHours(2), 0);
        inactive.Deactivate();

        Assert.Throws<DomainException>(() => past.Publish(Now));
        Assert.Throws<DomainException>(() => inactive.Publish(Now));
    }

    [Fact]
    public void ConfigureBackupSlots_PreservesSlotsAndChangesActiveSet()
    {
        var shift = Shift.Create("Service", null, null, Now.AddHours(1), Now.AddHours(2), 2);
        shift.ConfigureBackupSlots(0);
        shift.ConfigureBackupSlots(1);

        Assert.Equal(3, shift.Slots.Count);
        Assert.Single(shift.Slots, x => x.Kind == SlotKind.Backup && x.IsActive);
    }
}
