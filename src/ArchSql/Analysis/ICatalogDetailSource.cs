namespace ArchSql.Analysis;

/// <summary>Optional companion to ISchemaSource: a source that can supply catalog detail beyond
/// what CREATE DDL reconstruction carries — column width/precision/scale/collation, static index
/// definitions, and table row counts/storage size. Only the live database source implements it;
/// file scans leave this data absent, matching the degradation contract of IRuntimeStatsSource.</summary>
public interface ICatalogDetailSource
{
    CatalogDetail ReadCatalogDetail();
}

/// <summary>Raw catalog rows for one connection's worth of tables. Row counts/storage may be empty
/// when the login lacks permission to read them; columns and indexes come from ordinary catalog
/// views and are not permission-gated.</summary>
public sealed record CatalogDetail(
    List<Live.ColumnRow> Columns,
    List<Live.IndexColumnRow> Indexes,
    List<Live.TableStatsRow> TableStats);
