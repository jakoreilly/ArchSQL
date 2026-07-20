namespace ArchSql.Live;

/// <summary>The fixed, read-only catalog and DMV queries. All are constant SELECTs with no
/// interpolation — no caller input ever reaches these strings.</summary>
internal static class SqlServerQueries
{
    public const string Objects = """
        SELECT s.name AS SchemaName, o.name AS ObjectName, o.type AS TypeCode
        FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0 AND o.type IN ('U','V','P','FN','IF','TF','TR');
        """;

    public const string Columns = """
        SELECT s.name AS SchemaName, t.name AS TableName, c.name AS ColumnName,
               ty.name AS TypeName, c.max_length AS MaxLength, c.precision AS Prec,
               c.scale AS Scale, c.is_nullable AS IsNullable, c.is_identity AS IsIdentity,
               c.column_id AS Ordinal
        FROM sys.columns c
        JOIN sys.tables t ON t.object_id = c.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.types ty ON ty.user_type_id = c.user_type_id
        ORDER BY s.name, t.name, c.column_id;
        """;

    public const string PrimaryKeys = """
        SELECT s.name AS SchemaName, t.name AS TableName, col.name AS ColumnName, ic.key_ordinal AS KeyOrdinal
        FROM sys.key_constraints kc
        JOIN sys.tables t ON t.object_id = kc.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.index_columns ic ON ic.object_id = kc.parent_object_id AND ic.index_id = kc.unique_index_id
        JOIN sys.columns col ON col.object_id = ic.object_id AND col.column_id = ic.column_id
        WHERE kc.type = 'PK' ORDER BY s.name, t.name, ic.key_ordinal;
        """;

    public const string ForeignKeys = """
        SELECT s.name AS SchemaName, t.name AS TableName, rs.name AS RefSchema, rt.name AS RefTable,
               fk.name AS FkName, fk.delete_referential_action_desc AS OnDelete,
               pc.name AS FromColumn, rc.name AS ToColumn, fkc.constraint_column_id AS Ord
        FROM sys.foreign_keys fk
        JOIN sys.tables t ON t.object_id = fk.parent_object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        JOIN sys.tables rt ON rt.object_id = fk.referenced_object_id
        JOIN sys.schemas rs ON rs.schema_id = rt.schema_id
        JOIN sys.foreign_key_columns fkc ON fkc.constraint_object_id = fk.object_id
        JOIN sys.columns pc ON pc.object_id = fkc.parent_object_id AND pc.column_id = fkc.parent_column_id
        JOIN sys.columns rc ON rc.object_id = fkc.referenced_object_id AND rc.column_id = fkc.referenced_column_id
        ORDER BY s.name, t.name, fk.name, fkc.constraint_column_id;
        """;

    public const string Modules = """
        SELECT s.name AS SchemaName, o.name AS ObjectName, m.definition AS Definition
        FROM sys.sql_modules m
        JOIN sys.objects o ON o.object_id = m.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE o.is_ms_shipped = 0;
        """;

    public const string ProcStats = """
        SELECT s.name AS SchemaName, o.name AS ObjectName,
               ps.execution_count AS ExecCount,
               ps.total_worker_time / 1000 AS TotalWorkerMs,
               ps.total_logical_reads AS TotalLogicalReads
        FROM sys.dm_exec_procedure_stats ps
        JOIN sys.objects o ON o.object_id = ps.object_id
        JOIN sys.schemas s ON s.schema_id = o.schema_id
        WHERE ps.database_id = DB_ID();
        """;

    public const string IndexUsage = """
        SELECT s.name AS SchemaName, t.name AS TableName, ix.name AS IndexName,
               ISNULL(us.user_seeks,0) AS Seeks, ISNULL(us.user_scans,0) AS Scans,
               ISNULL(us.user_lookups,0) AS Lookups, ISNULL(us.user_updates,0) AS Updates
        FROM sys.indexes ix
        JOIN sys.tables t ON t.object_id = ix.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        LEFT JOIN sys.dm_db_index_usage_stats us
               ON us.object_id = ix.object_id AND us.index_id = ix.index_id AND us.database_id = DB_ID()
        WHERE ix.name IS NOT NULL;
        """;

    public const string MissingIndexes = """
        SELECT s.name AS SchemaName, t.name AS TableName,
               ISNULL(mid.equality_columns,'') AS EqCols,
               ISNULL(mid.inequality_columns,'') AS IneqCols,
               ISNULL(mid.included_columns,'') AS IncCols,
               migs.avg_total_user_cost * migs.avg_user_impact / 100.0 * (migs.user_seeks + migs.user_scans) AS ImpactScore
        FROM sys.dm_db_missing_index_group_stats migs
        JOIN sys.dm_db_missing_index_groups mig ON mig.index_group_handle = migs.group_handle
        JOIN sys.dm_db_missing_index_details mid ON mid.index_handle = mig.index_handle
        JOIN sys.tables t ON t.object_id = mid.object_id
        JOIN sys.schemas s ON s.schema_id = t.schema_id
        WHERE mid.database_id = DB_ID();
        """;
}
