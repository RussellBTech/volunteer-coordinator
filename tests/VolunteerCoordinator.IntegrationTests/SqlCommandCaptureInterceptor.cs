using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VolunteerCoordinator.IntegrationTests;

public sealed class SqlCommandCaptureInterceptor : DbCommandInterceptor
{
    private readonly List<string> _commands = [];
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _commands.Count;
            }
        }
    }

    public IReadOnlyList<string> Commands
    {
        get
        {
            lock (_gate)
            {
                return _commands.ToArray();
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _commands.Clear();
        }
    }

    private void Capture(DbCommand command)
    {
        lock (_gate)
        {
            _commands.Add(command.CommandText);
        }
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        Capture(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        Capture(command);
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        Capture(command);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Capture(command);
        return ValueTask.FromResult(result);
    }
}
