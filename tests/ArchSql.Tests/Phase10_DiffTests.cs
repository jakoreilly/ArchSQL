using ArchSql.Analysis;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase10_DiffTests
{
    private static SqlModel Model(params DbObject[] objects) =>
        new() { RootName = "x", SourcePath = "x", Objects = objects.ToList() };

    [Fact]
    public void Compute_DroppedTableIsBreaking()
    {
        var oldM = Model(new DbObject { Id = "dbo.orders", Schema = "dbo", Name = "Orders", Kind = "table", Dialect = "tsql" });
        var newM = Model();
        var changes = SchemaDiff.Compute(oldM, newM);
        var change = Assert.Single(changes);
        Assert.Equal("object-dropped", change.Kind);
        Assert.Equal(ChangeRisk.Breaking, change.Risk);
    }

    [Fact]
    public void Compute_NewNullableColumnIsSafeNewNotNullColumnIsBreaking()
    {
        var oldT = new DbObject { Id = "dbo.orders", Schema = "dbo", Name = "Orders", Kind = "table", Dialect = "tsql" };
        var newTNullable = oldT with { Columns = [new Column { Name = "Note", DataType = "varchar", Nullable = true }] };
        var changes = SchemaDiff.Compute(Model(oldT), Model(newTNullable));
        var change = Assert.Single(changes);
        Assert.Equal("column-added", change.Kind);
        Assert.Equal(ChangeRisk.Safe, change.Risk);

        var newTRequired = oldT with { Columns = [new Column { Name = "Note", DataType = "varchar", Nullable = false }] };
        var changes2 = SchemaDiff.Compute(Model(oldT), Model(newTRequired));
        var change2 = Assert.Single(changes2);
        Assert.Equal(ChangeRisk.Breaking, change2.Risk);
    }

    [Fact]
    public void Compute_NullToNotNullOnExistingColumnIsBreaking()
    {
        var oldT = new DbObject
        {
            Id = "dbo.orders", Schema = "dbo", Name = "Orders", Kind = "table", Dialect = "tsql",
            Columns = [new Column { Name = "Total", DataType = "decimal", Nullable = true }],
        };
        var newT = oldT with { Columns = [new Column { Name = "Total", DataType = "decimal", Nullable = false }] };
        var changes = SchemaDiff.Compute(Model(oldT), Model(newT));
        Assert.Contains(changes, c => c.Kind == "column-not-null-tightened" && c.Risk == ChangeRisk.Breaking);
    }

    [Theory]
    [InlineData("int", "INT", SqlTypeComparer.TypeChange.Same)]
    [InlineData("numeric", "decimal", SqlTypeComparer.TypeChange.Same)]
    [InlineData("int", "bigint", SqlTypeComparer.TypeChange.Compatible)]
    [InlineData("nvarchar", "varchar", SqlTypeComparer.TypeChange.Narrowing)]
    [InlineData("bigint", "int", SqlTypeComparer.TypeChange.Narrowing)]
    public void SqlTypeComparer_ClassifiesKnownTransitions(string oldType, string newType, SqlTypeComparer.TypeChange expected)
    {
        Assert.Equal(expected, SqlTypeComparer.Compare(oldType, newType));
    }

    [Fact]
    public void DiffBaseline_RoundTripsAndSuppressesBaselinedChange()
    {
        var oldT = new DbObject { Id = "dbo.orders", Schema = "dbo", Name = "Orders", Kind = "table", Dialect = "tsql" };
        var changes = SchemaDiff.Compute(Model(oldT), Model());
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            DiffBaseline.Write(changes, path);
            var suppressed = DiffBaseline.Load(path);
            Assert.Contains(DiffBaseline.Key(changes[0]), suppressed);

            var newBreaking = changes.Where(c => c.Risk == ChangeRisk.Breaking && !suppressed.Contains(DiffBaseline.Key(c))).ToList();
            Assert.Empty(newBreaking);
        }
        finally { File.Delete(path); }
    }
}
