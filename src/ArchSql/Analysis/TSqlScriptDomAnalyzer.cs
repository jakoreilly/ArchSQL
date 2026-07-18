using Microsoft.SqlServer.TransactSql.ScriptDom;
using ArchSql.Model;

namespace ArchSql.Analysis;

public sealed class TSqlScriptDomAnalyzer : ISqlDialectAnalyzer
{
    public string Dialect => "tsql";
    public bool CanHandle(string dialect) => dialect == "tsql";

    public SqlFileFacts Analyze(string relPath, string content)
    {
        var diagnostics = new List<string>();
        using var reader = new StringReader(content);
        var parser = new TSql160Parser(initialQuotedIdentifiers: true);
        var fragment = parser.Parse(reader, out IList<ParseError> errors);
        var parsedCleanly = errors.Count == 0;
        if (errors.Count > 0)
        {
            foreach (var e in errors.Take(5))
            {
                diagnostics.Add($"{relPath}({e.Line},{e.Column}): T-SQL parse: {e.Message}");
            }
        }

        var visitor = new TSqlFactsVisitor(relPath);
        fragment?.Accept(visitor);
        return visitor.ToFacts(parsedCleanly, diagnostics);
    }

    public string Format(string content) => TSqlFormatter.Format(content);
}

/// <summary>Walks a parsed T-SQL fragment collecting objects, foreign keys, and intra-file
/// dependencies. Each Visit override is small (Hard Constraint 5, cognitive complexity).</summary>
internal sealed class TSqlFactsVisitor(string relPath) : TSqlFragmentVisitor
{
    private readonly List<DbObject> _objects = [];
    private readonly List<ForeignKey> _foreignKeys = [];
    private readonly List<ObjectDep> _dependencies = [];
    private int _statementCount;
    private bool _hasCredential;

    // Tracks the object currently being defined, so nested body statements (SELECT/INSERT/...)
    // attribute their ObjectDep to the right FromObjectId.
    private string? _currentObjectId;

    public SqlFileFacts ToFacts(bool parsedCleanly, List<string> diagnostics) => new()
    {
        Dialect = "tsql",
        ParsedCleanly = parsedCleanly,
        StatementCount = _statementCount,
        HasCredential = _hasCredential,
        Objects = _objects,
        ForeignKeys = _foreignKeys,
        Dependencies = _dependencies,
        Diagnostics = diagnostics,
    };

    private static (string Schema, string Name) SplitSchemaObjectName(SchemaObjectName son)
    {
        var idents = son.Identifiers;
        if (idents.Count == 0) { return ("", ""); }
        var name = idents[^1].Value;
        var schema = idents.Count >= 2 ? idents[^2].Value : "";
        return (schema, name);
    }

    private static int LineOf(TSqlFragment fragment) => fragment.StartLine;

    public override void Visit(CreateTableStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.SchemaObjectName);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        var columns = new List<Column>();
        var pk = new List<string>();

        foreach (var def in node.Definition.ColumnDefinitions)
        {
            var colName = def.ColumnIdentifier.Value;
            var dataType = def.DataType is SqlDataTypeReference sdt
                ? sdt.Name.BaseIdentifier.Value
                : def.DataType?.Name?.BaseIdentifier?.Value ?? "";
            var nullable = true;
            var isIdentity = def.IdentityOptions is not null;
            foreach (var c in def.Constraints)
            {
                if (c is NullableConstraintDefinition nc) { nullable = !nc.Nullable == false && nc.Nullable; }
                if (c is UniqueConstraintDefinition { IsPrimaryKey: true }) { pk.Add(colName); }
            }
            columns.Add(new Column { Name = colName, DataType = dataType, Nullable = nullable, IsIdentity = isIdentity });
        }

        foreach (var c in node.Definition.TableConstraints)
        {
            if (c is UniqueConstraintDefinition { IsPrimaryKey: true } pkc)
            {
                foreach (var col in pkc.Columns) { pk.Add(col.Column.MultiPartIdentifier.Identifiers[^1].Value); }
            }
            if (c is ForeignKeyConstraintDefinition fk)
            {
                AddForeignKey(id, fk);
            }
        }

        _objects.Add(new DbObject
        {
            Id = id,
            Schema = schema,
            Name = name,
            Kind = "table",
            Dialect = "tsql",
            DefinedInSlug = relPath,
            DefinedAtLine = LineOf(node),
            Columns = columns,
            PrimaryKey = pk.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        });
    }

    private void AddForeignKey(string fromObjectId, ForeignKeyConstraintDefinition fk)
    {
        var (refSchema, refName) = SplitSchemaObjectName(fk.ReferenceTableName);
        var toId = IdentifierRules.NormalizeId(refSchema, refName, "tsql");
        _foreignKeys.Add(new ForeignKey
        {
            FromObjectId = fromObjectId,
            ToObjectId = toId, // resolved for real (empty when unresolved) in Phase 3's DependencyResolver
            FromColumns = fk.Columns.Select(c => c.Value).ToList(),
            ToColumns = fk.ReferencedTableColumns.Select(c => c.Value).ToList(),
            Name = fk.ConstraintIdentifier?.Value ?? "",
            OnDelete = fk.DeleteAction.ToString(),
        });
        _dependencies.Add(new ObjectDep { FromObjectId = fromObjectId, ToObjectId = toId, Kind = "fk" });
    }

    public override void Visit(CreateViewStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.SchemaObjectName);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        _objects.Add(new DbObject
        {
            Id = id, Schema = schema, Name = name, Kind = "view", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node),
            Body = node.SelectStatement?.ToString() ?? "",
        });
        WithCurrentObject(id, () => node.SelectStatement?.Accept(this));
    }

    public override void Visit(CreateProcedureStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.ProcedureReference.Name);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        DetectCredentialAndInjection(node, id);
        _objects.Add(new DbObject
        {
            Id = id, Schema = schema, Name = name, Kind = "procedure", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node),
        });
        WithCurrentObject(id, () =>
        {
            foreach (var body in node.StatementList?.Statements ?? []) { body.Accept(this); }
        });
    }

    public override void Visit(CreateFunctionStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.Name);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        _objects.Add(new DbObject
        {
            Id = id, Schema = schema, Name = name, Kind = "function", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node),
        });
        WithCurrentObject(id, () => node.AcceptChildren(this));
    }

    public override void Visit(CreateTriggerStatement node)
    {
        _statementCount++;
        var name = node.Name?.BaseIdentifier?.Value ?? "";
        var id = IdentifierRules.NormalizeId("", name, "tsql");
        _objects.Add(new DbObject
        {
            Id = id, Schema = "", Name = name, Kind = "trigger", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node),
        });
        WithCurrentObject(id, () =>
        {
            foreach (var body in node.StatementList?.Statements ?? []) { body.Accept(this); }
        });
    }

    public override void Visit(CreateIndexStatement node)
    {
        _statementCount++;
        var (schema, tableName) = SplitSchemaObjectName(node.OnName);
        var tableId = IdentifierRules.NormalizeId(schema, tableName, "tsql");
        var target = _objects.FirstOrDefault(o => o.Id == tableId);
        if (target is null) { return; }
        // Encode covered columns in the index entry ("name(col1,col2)") so SQL0007 (missing
        // index on FK column) can check coverage without a separate model field.
        var cols = string.Join(",", node.Columns.Select(c => c.Column.MultiPartIdentifier.Identifiers[^1].Value));
        target.Indexes.Add($"{node.Name?.Value ?? ""}({cols})");
    }

    public override void Visit(AlterTableAddTableElementStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.SchemaObjectName);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        foreach (var c in node.Definition.TableConstraints)
        {
            if (c is ForeignKeyConstraintDefinition fk) { AddForeignKey(id, fk); }
        }
    }

    public override void Visit(CreateLoginStatement node)
    {
        _statementCount++;
        // A password-based login source embeds a credential (Hard Constraint 2: capture the
        // FACT only — the Literal value itself is never read into HasCredential).
        if (node.Source is PasswordCreateLoginSource) { _hasCredential = true; }
    }

    public override void Visit(NamedTableReference node)
    {
        if (_currentObjectId is null) { return; }
        var (schema, name) = SplitSchemaObjectName(node.SchemaObject);
        if (name.Length == 0) { return; }
        var toId = IdentifierRules.NormalizeId(schema, name, "tsql");
        _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, ToObjectId = toId, Kind = "select" });
    }

    public override void Visit(ExecuteStatement node)
    {
        if (_currentObjectId is null) { return; }
        if (node.ExecuteSpecification?.ExecutableEntity is ExecutableProcedureReference epr
            && epr.ProcedureReference?.ProcedureReference?.Name is { } procName)
        {
            var (schema, name) = SplitSchemaObjectName(procName);
            var toId = IdentifierRules.NormalizeId(schema, name, "tsql");
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, ToObjectId = toId, Kind = "exec" });
        }
        node.AcceptChildren(this);
    }

    public override void Visit(InsertStatement node) { RecordDmlTarget(node.InsertSpecification?.Target, "insert"); node.AcceptChildren(this); }

    public override void Visit(UpdateStatement node)
    {
        RecordDmlTarget(node.UpdateSpecification?.Target, "update");
        if (_currentObjectId is not null && node.UpdateSpecification?.WhereClause is null)
        {
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, Kind = "update-nowhere" });
        }
        node.AcceptChildren(this);
    }

    public override void Visit(DeleteStatement node)
    {
        RecordDmlTarget(node.DeleteSpecification?.Target, "delete");
        if (_currentObjectId is not null && node.DeleteSpecification?.WhereClause is null)
        {
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, Kind = "delete-nowhere" });
        }
        node.AcceptChildren(this);
    }

    public override void Visit(SelectStarExpression node)
    {
        if (_currentObjectId is not null)
        {
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, Kind = "select-star" });
        }
    }

    private void RecordDmlTarget(TableReference? target, string kind)
    {
        if (_currentObjectId is null || target is not NamedTableReference ntr) { return; }
        var (schema, name) = SplitSchemaObjectName(ntr.SchemaObject);
        if (name.Length == 0) { return; }
        var toId = IdentifierRules.NormalizeId(schema, name, "tsql");
        _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, ToObjectId = toId, Kind = kind });
    }

    /// <summary>Flags SQL0002 precursor data: a dynamic-SQL EXEC/sp_executesql whose argument is
    /// built from string concatenation (+ / CONCAT) of a variable rather than a static literal.
    /// This is a report about the SCANNED sql, never executed here (Hard Constraint 1).</summary>
    private void DetectCredentialAndInjection(CreateProcedureStatement node, string objectId)
    {
        var walker = new DynamicSqlWalker(objectId);
        node.AcceptChildren(walker);
        _dependencies.AddRange(walker.InjectionRiskDeps);
    }

    private void WithCurrentObject(string id, Action body)
    {
        var previous = _currentObjectId;
        _currentObjectId = id;
        try { body(); }
        finally { _currentObjectId = previous; }
    }
}

/// <summary>Detects dynamic SQL built from concatenated variables reaching EXEC/sp_executesql —
/// the SQL0002 injection-risk signal (Phase 4 consumes ObjectDep{Kind="exec-dynamic"}).</summary>
internal sealed class DynamicSqlWalker(string objectId) : TSqlFragmentVisitor
{
    public List<ObjectDep> InjectionRiskDeps { get; } = [];

    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification?.ExecutableEntity is ExecutableStringList list)
        {
            foreach (var s in list.Strings)
            {
                if (ContainsConcatenation(s))
                {
                    InjectionRiskDeps.Add(new ObjectDep { FromObjectId = objectId, Kind = "exec-dynamic" });
                    break;
                }
            }
        }
    }

    private static bool ContainsConcatenation(ScalarExpression expr) =>
        expr is BinaryExpression { BinaryExpressionType: BinaryExpressionType.Add };
}
