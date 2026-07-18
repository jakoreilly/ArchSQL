using ArchSql.Analysis;
using ArchSql.Model;
using Xunit;

namespace ArchSql.Tests;

public class Phase3_ResolveTests
{
    [Fact]
    public void Resolve_CrossFileReferenceGetsResolvedToObjectId()
    {
        var tableB = new DbObject { Id = "dbo.orders", Schema = "dbo", Name = "Orders", Kind = "table", Dialect = "tsql", DefinedInSlug = "b" };
        var procA = new DbObject { Id = "dbo.usp_get", Schema = "dbo", Name = "usp_Get", Kind = "procedure", Dialect = "tsql", DefinedInSlug = "a" };
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Objects = [procA, tableB],
            Dependencies = [new ObjectDep { FromObjectId = "dbo.usp_get", ToObjectId = "dbo.orders", Kind = "select" }],
        };

        var resolved = DependencyResolver.Resolve(model);

        var dep = Assert.Single(resolved.Dependencies);
        Assert.Equal("dbo.orders", dep.ToObjectId);
        Assert.Empty(dep.ExternalTarget);
        Assert.Contains("dbo.orders", resolved.Objects.Single(o => o.Id == "dbo.usp_get").ReferencesObjectIds);
    }

    [Fact]
    public void Resolve_FkToAbsentTableYieldsEmptyToObjectIdAndExternalTarget()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            ForeignKeys = [new ForeignKey { FromObjectId = "dbo.orders", ToObjectId = "dbo.missing" }],
        };

        var resolved = DependencyResolver.Resolve(model);

        var fk = Assert.Single(resolved.ForeignKeys);
        Assert.Empty(fk.ToObjectId);
    }

    [Fact]
    public void SqlMetrics_TableWithoutPrimaryKeyIsReported()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Objects =
            [
                new DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "table", Dialect = "tsql", PrimaryKey = ["Id"] },
                new DbObject { Id = "dbo.b", Schema = "dbo", Name = "B", Kind = "table", Dialect = "tsql" },
            ],
        };

        var noPk = SqlMetrics.TablesWithoutPrimaryKey(model);
        Assert.Single(noPk);
        Assert.Equal("dbo.b", noPk[0].Id);
    }

    [Fact]
    public void SqlMetrics_FanInCountsMatchResolvedReferences()
    {
        var model = new SqlModel
        {
            RootName = "x",
            SourcePath = "x",
            Objects =
            [
                new DbObject { Id = "dbo.a", Schema = "dbo", Name = "A", Kind = "procedure", Dialect = "tsql", ReferencesObjectIds = ["dbo.b"] },
                new DbObject { Id = "dbo.c", Schema = "dbo", Name = "C", Kind = "procedure", Dialect = "tsql", ReferencesObjectIds = ["dbo.b"] },
                new DbObject { Id = "dbo.b", Schema = "dbo", Name = "B", Kind = "table", Dialect = "tsql" },
            ],
        };

        var fanIn = SqlMetrics.FanIn(model);
        Assert.Equal(2, fanIn["dbo.b"]);
        Assert.Equal(0, fanIn["dbo.a"]);
    }
}
