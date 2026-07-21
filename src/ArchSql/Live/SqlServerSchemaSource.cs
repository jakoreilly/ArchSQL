using ArchSql.Analysis;
using ArchSql.Model;
using Microsoft.Data.SqlClient;

namespace ArchSql.Live;

/// <summary>Read-only SQL Server schema source. Read() reconstructs CREATE DDL from catalog views
/// so the live schema flows through the existing analyzer; ReadRuntime() layers DMV facts on top,
/// degrading to Available=false when the login lacks VIEW DATABASE STATE.
///
/// Read-only by discipline: every command is a constant SELECT run under READ UNCOMMITTED, the
/// connection opens with ApplicationIntent=ReadOnly, and no DML/DDL is ever issued. This is not a
/// cryptographic guarantee — operators should connect with a least-privilege read-only login.
///
/// This is the thin I/O shell; all reconstruction/aggregation lives in the pure helpers (LiveDdl,
/// RuntimeAggregate, SqlTypeText) which are unit-tested without a database.</summary>
public sealed class SqlServerSchemaSource(string connectionString, int timeoutSeconds, List<string> diagnostics)
    : ISchemaSource, IRuntimeStatsSource, ICatalogDetailSource
{
    // SQL Server permission-denied for DMVs: 297 (VIEW SERVER STATE), 300 (generic permission).
    private static readonly int[] PermissionErrors = [297, 300];

    private string BuildConnectionString()
    {
        var b = new SqlConnectionStringBuilder(connectionString)
        {
            ApplicationIntent = ApplicationIntent.ReadOnly,
            ConnectTimeout = timeoutSeconds,
            ApplicationName = "ArchSql",
        };
        return b.ConnectionString;
    }

    public IEnumerable<(string RelPath, string Content, string Dialect)> Read()
    {
        using var conn = OpenReadOnly();
        var objects = Query(conn, SqlServerQueries.Objects, r => new ObjectRow(Str(r, 0), Str(r, 1), Str(r, 2)));
        var columns = Query(conn, SqlServerQueries.Columns, r => new ColumnRow(
            Str(r, 0), Str(r, 1), Str(r, 2), Str(r, 3), r.GetInt16(4), r.GetByte(5), r.GetByte(6),
            r.GetBoolean(7), r.GetBoolean(8), r.GetInt32(9), Str(r, 10)));
        var pks = Query(conn, SqlServerQueries.PrimaryKeys, r => new PkRow(Str(r, 0), Str(r, 1), Str(r, 2), r.GetByte(3)));
        var fks = Query(conn, SqlServerQueries.ForeignKeys, r => new FkRow(
            Str(r, 0), Str(r, 1), Str(r, 2), Str(r, 3), Str(r, 4), Str(r, 5), Str(r, 6), Str(r, 7), r.GetInt32(8)));
        var modules = Query(conn, SqlServerQueries.Modules, r => new ModuleRow(Str(r, 0), Str(r, 1), Str(r, 2)));

        foreach (var (relPath, content) in LiveDdl.BuildUnits(objects, columns, pks, fks, modules))
        {
            yield return (relPath, content, "tsql");
        }
    }

    public RuntimeStats ReadRuntime()
    {
        try
        {
            using var conn = OpenReadOnly();
            var (procStats, procOk) = TryQuery(conn, SqlServerQueries.ProcStats, r => new ProcStatRow(
                Str(r, 0), Str(r, 1), r.GetInt64(2), r.GetInt64(3), r.GetInt64(4)));
            var (indexUsage, idxOk) = TryQuery(conn, SqlServerQueries.IndexUsage, r => new IndexUsageRow(
                Str(r, 0), Str(r, 1), Str(r, 2), r.GetInt64(3), r.GetInt64(4), r.GetInt64(5), r.GetInt64(6)));
            var (missing, missOk) = TryQuery(conn, SqlServerQueries.MissingIndexes, r => new MissingIndexRow(
                Str(r, 0), Str(r, 1), Str(r, 2), Str(r, 3), Str(r, 4), r.IsDBNull(5) ? 0 : r.GetDouble(5)));

            var (backups, backupOk) = TryQuery(conn, SqlServerQueries.LastBackup, r => new BackupRow(r.GetDateTime(0)));
            var (statsAge, statsOk) = TryQuery(conn, SqlServerQueries.StatsAge, r => new StatsAgeRow(
                Str(r, 0), Str(r, 1), Str(r, 2), r.GetDateTime(3)));
            var (fragmentation, fragOk) = TryQuery(conn, SqlServerQueries.Fragmentation, r => new FragmentationRow(
                Str(r, 0), Str(r, 1), Str(r, 2), r.GetDouble(3), r.GetInt64(4)));

            if (!procOk && !idxOk && !missOk)
            {
                var runtime = RuntimeAggregate.Build([], [], [], available: false,
                    "Runtime data unavailable: the login lacks VIEW DATABASE STATE permission.");
                var maintenance = MaintenanceAggregate.Build(backups, statsAge, fragmentation, backupOk || statsOk || fragOk, DateTime.UtcNow);
                return runtime with { Maintenance = maintenance };
            }
            var stats = RuntimeAggregate.Build(procStats, indexUsage, missing, available: true);
            return stats with { Maintenance = MaintenanceAggregate.Build(backups, statsAge, fragmentation, backupOk || statsOk || fragOk, DateTime.UtcNow) };
        }
        catch (SqlException ex)
        {
            diagnostics.Add($"Runtime stats skipped: {ex.Message}");
            return RuntimeAggregate.Build([], [], [], available: false, "Runtime data unavailable: " + ex.Message);
        }
    }

    public CatalogDetail ReadCatalogDetail()
    {
        using var conn = OpenReadOnly();
        var columns = Query(conn, SqlServerQueries.Columns, r => new ColumnRow(
            Str(r, 0), Str(r, 1), Str(r, 2), Str(r, 3), r.GetInt16(4), r.GetByte(5), r.GetByte(6),
            r.GetBoolean(7), r.GetBoolean(8), r.GetInt32(9), Str(r, 10)));
        var indexes = Query(conn, SqlServerQueries.IndexInventory, r => new IndexColumnRow(
            Str(r, 0), Str(r, 1), Str(r, 2), r.GetBoolean(3), r.GetBoolean(4), Str(r, 5),
            r.GetBoolean(6), Str(r, 7), r.GetByte(8), r.GetBoolean(9)));
        // Row counts/storage size need VIEW DATABASE STATE (like the runtime DMVs); degrade to an
        // empty list rather than fail the whole catalog read.
        var (tableStats, _) = TryQuery(conn, SqlServerQueries.TableStats, r => new TableStatsRow(
            Str(r, 0), Str(r, 1), r.GetInt64(2), r.GetInt64(3)));
        return new CatalogDetail(columns, indexes, tableStats);
    }

    private SqlConnection OpenReadOnly()
    {
        var conn = new SqlConnection(BuildConnectionString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;";
        cmd.CommandTimeout = timeoutSeconds;
        cmd.ExecuteNonQuery();
        return conn;
    }

    private List<T> Query<T>(SqlConnection conn, string sql, Func<SqlDataReader, T> map)
    {
        var list = new List<T>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = timeoutSeconds;
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) { list.Add(map(reader)); }
        return list;
    }

    /// <summary>Like Query, but a permission-denied SqlException yields (empty, false) so the caller
    /// can degrade rather than fail. Other SqlExceptions propagate.</summary>
    private (List<T> Rows, bool Ok) TryQuery<T>(SqlConnection conn, string sql, Func<SqlDataReader, T> map)
    {
        try { return (Query(conn, sql, map), true); }
        catch (SqlException ex) when (Array.IndexOf(PermissionErrors, ex.Number) >= 0)
        {
            diagnostics.Add($"Skipped a runtime query (permission denied): {ex.Message}");
            return ([], false);
        }
    }

    private static string Str(SqlDataReader r, int i) => r.IsDBNull(i) ? "" : r.GetValue(i).ToString() ?? "";
}
