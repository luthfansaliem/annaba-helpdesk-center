using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AnnabaDigital.Infrastructure.Persistence;

/// <summary>Counts the SQL commands the application sends (one atomic increment each). Lets tests hold a "query budget" for hot endpoints so an extra round trip per request is noticed.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    private static long _total;
    public static long Total => Interlocked.Read(ref _total);
    /// <summary>Diagnostics hook (tests, profiling): called with each command's SQL text.</summary>
    public static Action<string>? Trace { get; set; }
    private static void Seen(DbCommand c) { Interlocked.Increment(ref _total); Trace?.Invoke(c.CommandText); }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result) { Seen(command); return result; }
    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default) { Seen(command); return new(result); }
    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result) { Seen(command); return result; }
    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default) { Seen(command); return new(result); }
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result) { Seen(command); return result; }
    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default) { Seen(command); return new(result); }
}
