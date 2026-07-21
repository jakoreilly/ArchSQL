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
/// dependencies. Each Visit override is kept small to limit cognitive complexity.</summary>
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
        // Default the schema for display when the statement left it unqualified. T-SQL resolves
        // unqualified objects to dbo, and NormalizeId already keys the id under dbo, so populating
        // Schema here keeps display ("schema.name") consistent with the id and avoids a leading-dot
        // label like ".MyProc". Only default a real object name (not a temp table / empty).
        if (schema.Length == 0 && name.Length > 0 && name[0] != '#' && name[0] != '@') { schema = "dbo"; }
        return (schema, name);
    }

    private static int LineOf(TSqlFragment fragment) => fragment.StartLine;

    /// <summary>The verbatim source text of a fragment, reconstructed from its token span. Used to
    /// score cyclomatic complexity of procedure/function/trigger bodies without storing the full
    /// body text on the model (which would bloat model.json for large databases).</summary>
    private static string SourceOf(TSqlFragment node)
    {
        var stream = node.ScriptTokenStream;
        if (stream is null || node.FirstTokenIndex < 0 || node.LastTokenIndex < 0) { return ""; }
        var last = Math.Min(node.LastTokenIndex, stream.Count - 1);
        var parts = new List<string>();
        for (var i = node.FirstTokenIndex; i <= last; i++) { parts.Add(stream[i].Text); }
        return string.Concat(parts);
    }

    public override void Visit(CreateTableStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.SchemaObjectName);
        // Temp tables (#name, ##name) and table variables are session-local, not schema objects.
        // They are declared inside procedure bodies and frequently reuse the same names across
        // modules, so emitting them would pollute the inventory and collide on the id-keyed model.
        if (name.Length == 0 || IsTempOrVariable(name)) { return; }
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
            ToObjectId = toId, // resolved later by DependencyResolver (empty when unresolved)
            FromColumns = fk.Columns.Select(c => c.Value).ToList(),
            ToColumns = fk.ReferencedTableColumns.Select(c => c.Value).ToList(),
            Name = fk.ConstraintIdentifier?.Value ?? "",
            OnDelete = fk.DeleteAction.ToString(),
        });
        _dependencies.Add(new ObjectDep { FromObjectId = fromObjectId, ToObjectId = toId, Kind = "fk" });
    }

    // GOTCHA: these four container statements (view/procedure/function/trigger) each explicitly
    // walk into their own body to attribute nested dependencies to the right _currentObjectId.
    // TSqlFragmentVisitor's base ExplicitVisit(T) always does "Visit(node); node.AcceptChildren(this)"
    // — so overriding Visit(T) here would make the framework ALSO auto-walk the body a SECOND time
    // afterward, with _currentObjectId already reset to null. That's merely noisy for dependencies,
    // but for a nested CREATE TABLE #temp it re-adds the same #temp DbObject twice, crashing
    // DependencyResolver's ToDictionary with a duplicate key. Overriding ExplicitVisit instead of
    // Visit takes full control of traversal — no implicit second pass — which is the correct
    // ScriptDom idiom whenever a Visit override needs to control (not just react to) child walking.
    public override void ExplicitVisit(CreateViewStatement node)
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

    public override void ExplicitVisit(CreateProcedureStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.ProcedureReference.Name);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        DetectCredentialAndInjection(node, id);
        var source = SourceOf(node);
        _objects.Add(new DbObject
        {
            Id = id, Schema = schema, Name = name, Kind = "procedure", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node), Cyclomatic = SqlMetrics.Cyclomatic(source),
            CodeFlags = CodeFlagsScanner.Scan(source),
        });
        WithCurrentObject(id, () =>
        {
            foreach (var body in node.StatementList?.Statements ?? []) { body.Accept(this); }
        });
    }

    public override void ExplicitVisit(CreateFunctionStatement node)
    {
        _statementCount++;
        var (schema, name) = SplitSchemaObjectName(node.Name);
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        var source = SourceOf(node);
        _objects.Add(new DbObject
        {
            Id = id, Schema = schema, Name = name, Kind = "function", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node), Cyclomatic = SqlMetrics.Cyclomatic(source),
            CodeFlags = CodeFlagsScanner.Scan(source),
        });
        WithCurrentObject(id, () => node.AcceptChildren(this));
    }

    public override void ExplicitVisit(CreateTriggerStatement node)
    {
        _statementCount++;
        var (schema, name) = node.Name is not null ? SplitSchemaObjectName(node.Name) : ("dbo", "");
        var id = IdentifierRules.NormalizeId(schema, name, "tsql");
        var source = SourceOf(node);
        _objects.Add(new DbObject
        {
            Id = id, Schema = schema, Name = name, Kind = "trigger", Dialect = "tsql",
            DefinedInSlug = relPath, DefinedAtLine = LineOf(node), Cyclomatic = SqlMetrics.Cyclomatic(source),
            CodeFlags = CodeFlagsScanner.Scan(source),
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
        // A password-based login source embeds a credential; record only that fact —
        // the literal value itself is never read into HasCredential.
        if (node.Source is PasswordCreateLoginSource) { _hasCredential = true; }
    }

    public override void Visit(NamedTableReference node)
    {
        if (_currentObjectId is null || ReferenceEquals(node, _dmlTargetToSkip)) { return; }
        var (schema, name) = SplitSchemaObjectName(node.SchemaObject);
        if (name.Length == 0 || IsTempOrVariable(name)) { return; }
        var toId = IdentifierRules.NormalizeId(schema, name, "tsql");
        _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, ToObjectId = toId, Kind = "read" });
    }

    /// <summary>#temp tables and @table variables must be excluded from CRUD/dependency tracking —
    /// otherwise the CRUD matrix fills with noise for scratch state that isn't a real schema object.</summary>
    private static bool IsTempOrVariable(string name) => name.Length > 0 && (name[0] == '#' || name[0] == '@');

    // Alias -> real (schema,name), built per UPDATE/DELETE from its FromClause. T-SQL lets the SET
    // target be an alias defined in FROM (UPDATE t SET ... FROM Orders AS t); without this map the
    // write is attributed to a table literally named "t".
    private static Dictionary<string, SchemaObjectName> BuildAliasMap(FromClause? from)
    {
        var map = new Dictionary<string, SchemaObjectName>(StringComparer.OrdinalIgnoreCase);
        if (from is null) { return map; }
        foreach (var tr in from.TableReferences) { CollectAliases(tr, map); }
        return map;
    }

    private static void CollectAliases(TableReference tr, Dictionary<string, SchemaObjectName> map)
    {
        if (tr is NamedTableReference ntr && ntr.Alias is { } a) { map[a.Value] = ntr.SchemaObject; }
        if (tr is QualifiedJoin qj) { CollectAliases(qj.FirstTableReference, map); CollectAliases(qj.SecondTableReference, map); }
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

    // Tracks the raw DML target node RecordDmlTarget already handled, so the generic child
    // traversal doesn't ALSO visit it as an ordinary NamedTableReference. This matters most for an
    // UPDATE/DELETE whose target is written through a FROM-clause alias: the target's SchemaObject
    // is then the single-part alias, and without this guard the traversal would record a bogus read
    // dependency on a table with the alias's name alongside the correctly resolved write target.
    //
    // GOTCHA: TSqlFragmentVisitor's base ExplicitVisit(T) always calls Visit(node) and THEN
    // unconditionally calls node.AcceptChildren(this) itself, regardless of what Visit() does. So
    // these DML visitors must NOT also call node.AcceptChildren(this) explicitly — doing so visits
    // every child twice (once from the explicit call, once from the framework's automatic one),
    // which duplicates every "read" dependency and — worse — re-visits Target a second time after
    // any try/finally has already reset the skip guard. Just set the field; the framework's own
    // single automatic pass is what actually walks FROM/WHERE/etc.
    private TableReference? _dmlTargetToSkip;

    public override void Visit(InsertStatement node)
    {
        var target = node.InsertSpecification?.Target;
        RecordDmlTarget(target, "insert", null);
        _dmlTargetToSkip = target;
    }

    public override void Visit(UpdateStatement node)
    {
        var spec = node.UpdateSpecification;
        RecordDmlTarget(spec?.Target, "update", spec?.FromClause);
        if (_currentObjectId is not null && spec?.WhereClause is null)
        {
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, Kind = "update-nowhere" });
        }
        _dmlTargetToSkip = spec?.Target;
    }

    public override void Visit(DeleteStatement node)
    {
        var spec = node.DeleteSpecification;
        RecordDmlTarget(spec?.Target, "delete", spec?.FromClause);
        if (_currentObjectId is not null && spec?.WhereClause is null)
        {
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, Kind = "delete-nowhere" });
        }
        _dmlTargetToSkip = spec?.Target;
    }

    /// <summary>MERGE writes its target via WHEN [NOT] MATCHED action clauses, producing C/U/D
    /// simultaneously depending on which actions are present.</summary>
    public override void Visit(MergeStatement node)
    {
        var spec = node.MergeSpecification;
        if (_currentObjectId is null || spec?.Target is not NamedTableReference ntr) { _dmlTargetToSkip = spec?.Target; return; }
        var (schema, name) = SplitSchemaObjectName(ntr.SchemaObject);
        if (name.Length == 0 || IsTempOrVariable(name)) { _dmlTargetToSkip = spec.Target; return; }
        var toId = IdentifierRules.NormalizeId(schema, name, "tsql");
        foreach (var clause in spec.ActionClauses)
        {
            var kind = clause.Action switch
            {
                InsertMergeAction => "insert",
                UpdateMergeAction => "update",
                DeleteMergeAction => "delete",
                _ => "",
            };
            if (kind.Length > 0) { _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, ToObjectId = toId, Kind = kind }); }
        }
        _dmlTargetToSkip = spec.Target; // still records USING-source reads via the framework's own AcceptChildren pass
    }

    public override void Visit(SelectStarExpression node)
    {
        if (_currentObjectId is not null)
        {
            _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, Kind = "select-star" });
        }
    }

    private void RecordDmlTarget(TableReference? target, string kind, FromClause? from)
    {
        if (_currentObjectId is null || target is not NamedTableReference ntr) { return; }
        var son = ntr.SchemaObject;
        if (son.Identifiers.Count == 1 && from is not null
            && BuildAliasMap(from).TryGetValue(son.Identifiers[0].Value, out var real)) { son = real; }
        var (schema, name) = SplitSchemaObjectName(son);
        if (name.Length == 0 || IsTempOrVariable(name)) { return; }
        var toId = IdentifierRules.NormalizeId(schema, name, "tsql");
        _dependencies.Add(new ObjectDep { FromObjectId = _currentObjectId, ToObjectId = toId, Kind = kind });
    }

    /// <summary>Flags SQL0002 precursor data: a dynamic-SQL EXEC/sp_executesql whose argument is
    /// built from string concatenation (+ / CONCAT) of a variable rather than a static literal.
    /// This is a report about the scanned SQL; nothing here is ever executed.</summary>
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

/// <summary>Detects dynamic SQL built from concatenation reaching EXEC(...) — the SQL0002
/// injection-risk signal (CrudMatrix and the lint rules consume ObjectDep{Kind="exec-dynamic"}).
///
/// GOTCHA: EXEC('literal' + @var) is NOT parsed as a single scalar expression containing a
/// BinaryExpression — ScriptDom's EXEC(...) grammar treats the top-level '+' as its own list
/// separator, so 'literal' and @var arrive as two SEPARATE entries in ExecutableStringList.Strings
/// (verified empirically: a single-item Strings list is a static EXEC('literal') or EXEC(@var); a
/// multi-item list only ever arises from '+'-joined concatenation). So Strings.Count > 1 IS the
/// concatenation signal — checking for a nested BinaryExpression node (the previous approach) can
/// never match any real T-SQL and was dead code.</summary>
internal sealed class DynamicSqlWalker(string objectId) : TSqlFragmentVisitor
{
    public List<ObjectDep> InjectionRiskDeps { get; } = [];

    public override void Visit(ExecuteStatement node)
    {
        if (node.ExecuteSpecification?.ExecutableEntity is ExecutableStringList { Strings.Count: > 1 })
        {
            InjectionRiskDeps.Add(new ObjectDep { FromObjectId = objectId, Kind = "exec-dynamic" });
        }
    }
}
