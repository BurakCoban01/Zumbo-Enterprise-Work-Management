using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zumbo.ArchitectureTests.RefactorValidation;

internal static class RefactorSemanticInventory
{
    internal const string BaselineCommit = "2931debd21ba3e16e5c3f1bda4bbbbff035711e3";
    internal const string RefactorSnapshotCommit = "b3edce4a7365fc87ad8d9af218ac195f11cf483e";

    internal static Snapshot ReadGitSnapshot(string repositoryDirectory, string gitRef)
        => BuildSnapshot(gitRef, RefactorSourceReader.ReadGit(repositoryDirectory, gitRef));

    internal static Snapshot ReadWorkingTree(string projectDirectory)
        => BuildSnapshot("working-tree", RefactorSourceReader.ReadWorkingTree(projectDirectory));

    internal static Comparison Compare(
        Snapshot baseline,
        Snapshot target,
        IReadOnlyDictionary<string, string>? relocatedTypes = null)
    {
        var targetTypes = target.Types.ToDictionary(type => type.Key, StringComparer.Ordinal);
        var baselineTypeKeys = baseline.Types.Select(type => type.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var (baselineKey, targetKey) in relocatedTypes ?? new Dictionary<string, string>())
        {
            if (!baselineTypeKeys.Contains(baselineKey))
            {
                throw new InvalidOperationException($"Relocated baseline type does not exist: {baselineKey}");
            }

            if (!targetTypes.ContainsKey(targetKey))
            {
                throw new InvalidOperationException($"Relocated target type does not exist: {targetKey}");
            }

            if (!string.Equals(
                    baselineKey[(baselineKey.IndexOf('|') + 1)..],
                    targetKey[(targetKey.IndexOf('|') + 1)..],
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Relocated type must preserve its full type name: {baselineKey} -> {targetKey}");
            }
        }

        var missingTypes = new List<TypeDifference>();
        var typeSignatureDifferences = new List<TypeDifference>();
        var missingMembers = new List<MemberDifference>();
        var memberSignatureDifferences = new List<MemberDifference>();
        var bodyDifferences = new List<MemberDifference>();
        var addedMembers = new List<AddedMember>();
        var matchedMembers = 0;
        var matchedTargetTypeKeys = baseline.Types
            .Select(type => relocatedTypes?.GetValueOrDefault(type.Key) ?? type.Key)
            .ToHashSet(StringComparer.Ordinal);
        var addedTypes = target.Types
            .Where(type => !matchedTargetTypeKeys.Contains(type.Key))
            .OrderBy(type => type.Key, StringComparer.Ordinal)
            .ToArray();

        foreach (var baselineType in baseline.Types)
        {
            var targetKey = relocatedTypes?.GetValueOrDefault(baselineType.Key) ?? baselineType.Key;
            if (!targetTypes.TryGetValue(targetKey, out var targetType))
            {
                missingTypes.Add(new TypeDifference(
                    baselineType.Key,
                    baselineType.Files,
                    [],
                    baselineType.Signature,
                    null));
                continue;
            }

            if (!string.Equals(baselineType.Signature, targetType.Signature, StringComparison.Ordinal))
            {
                typeSignatureDifferences.Add(new TypeDifference(
                    baselineType.Key,
                    baselineType.Files,
                    targetType.Files,
                    baselineType.Signature,
                    targetType.Signature));
            }

            var targetMembers = targetType.Members
                .GroupBy(member => member.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

            foreach (var baselineMember in baselineType.Members)
            {
                var differenceId = $"{baselineType.Key}|{baselineMember.Key}";
                if (!targetMembers.TryGetValue(baselineMember.Key, out var candidates)
                    || candidates.Count == 0)
                {
                    missingMembers.Add(MemberDifference.Missing(
                        differenceId,
                        baselineType.Key,
                        baselineMember));
                    continue;
                }

                var exact = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Signature, baselineMember.Signature, StringComparison.Ordinal)
                    && string.Equals(candidate.Behavior, baselineMember.Behavior, StringComparison.Ordinal));
                if (exact is not null)
                {
                    matchedMembers++;
                    candidates.Remove(exact);
                    continue;
                }

                var matchingSignature = candidates.FirstOrDefault(candidate =>
                    string.Equals(candidate.Signature, baselineMember.Signature, StringComparison.Ordinal));
                if (matchingSignature is not null)
                {
                    matchedMembers++;
                    bodyDifferences.Add(MemberDifference.Changed(
                        differenceId,
                        baselineType.Key,
                        baselineMember,
                        matchingSignature));
                    candidates.Remove(matchingSignature);
                    continue;
                }

                var candidate = candidates[0];
                memberSignatureDifferences.Add(MemberDifference.Changed(
                    differenceId,
                    baselineType.Key,
                    baselineMember,
                    candidate));
                candidates.RemoveAt(0);
            }

            addedMembers.AddRange(targetMembers.Values
                .SelectMany(candidates => candidates)
                .Select(member => new AddedMember(
                    targetType.Key,
                    member.Key,
                    member.File,
                    member.Signature)));
        }

        return new Comparison(
            baseline,
            target,
            new Dictionary<string, string>(relocatedTypes ?? new Dictionary<string, string>(), StringComparer.Ordinal),
            matchedMembers,
            addedTypes,
            addedMembers
                .OrderBy(item => item.Type, StringComparer.Ordinal)
                .ThenBy(item => item.Member, StringComparer.Ordinal)
                .ThenBy(item => item.File, StringComparer.Ordinal)
                .ToArray(),
            missingTypes.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            typeSignatureDifferences.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            missingMembers.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            memberSignatureDifferences.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray(),
            bodyDifferences.OrderBy(item => item.Id, StringComparer.Ordinal).ToArray());
    }

    private static Snapshot BuildSnapshot(
        string reference,
        IReadOnlyCollection<RefactorSourceReader.SourceFile> files)
    {
        var builders = new Dictionary<string, TypeBuilder>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var root = CSharpSyntaxTree.ParseText(
                    file.Content,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Latest))
                .GetCompilationUnitRoot();

            foreach (var declaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                AddType(builders, file.Path, declaration);
            }
        }

        return new Snapshot(
            reference,
            files.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray(),
            builders.Values
                .Select(builder => builder.Build())
                .OrderBy(type => type.Key, StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddType(
        IDictionary<string, TypeBuilder> builders,
        string path,
        BaseTypeDeclarationSyntax declaration)
    {
        var project = ProjectName(path);
        var fullName = FullTypeName(declaration);
        var key = $"{project}|{fullName}";
        if (!builders.TryGetValue(key, out var builder))
        {
            builder = new TypeBuilder(
                key,
                project,
                fullName,
                TypeKind(declaration),
                TypeSignature(declaration),
                declaration.BaseList is not null);
            builders.Add(key, builder);
        }
        else
        {
            builder.ConsiderSignature(
                TypeSignature(declaration),
                declaration.BaseList is not null);
        }

        builder.Files.Add(path);
        foreach (var member in DirectMembers(declaration))
        {
            foreach (var element in MemberElements(path, member))
            {
                builder.Members.Add(element);
            }
        }

        if (declaration is EnumDeclarationSyntax enumDeclaration)
        {
            foreach (var member in enumDeclaration.Members)
            {
                builder.Members.Add(new MemberElement(
                    $"enum:{member.Identifier.ValueText}",
                    Signature(member.AttributeLists, [], member.Identifier.ValueText),
                    Normalize(member.EqualsValue),
                    path));
            }
        }

        var primaryConstructor = declaration.ChildNodes().OfType<ParameterListSyntax>().FirstOrDefault();
        if (primaryConstructor is not null)
        {
            builder.Members.Add(new MemberElement(
                $"primary-ctor:{Normalize(primaryConstructor)}",
                Normalize(primaryConstructor),
                string.Empty,
                path));
        }
    }

    private static IEnumerable<MemberDeclarationSyntax> DirectMembers(BaseTypeDeclarationSyntax declaration) =>
        declaration switch
        {
            TypeDeclarationSyntax type => type.Members.Where(member => member is not BaseTypeDeclarationSyntax),
            _ => []
        };

    private static IEnumerable<MemberElement> MemberElements(string path, MemberDeclarationSyntax member)
    {
        switch (member)
        {
            case MethodDeclarationSyntax method:
                yield return new MemberElement(
                    $"method:{Normalize(method.ExplicitInterfaceSpecifier)}{method.Identifier.ValueText}"
                    + $"{Normalize(method.TypeParameterList)}:{Normalize(method.ParameterList)}:{Normalize(method.ReturnType)}",
                    Signature(method.AttributeLists, method.Modifiers,
                        Normalize(method.ReturnType), Normalize(method.ExplicitInterfaceSpecifier),
                        method.Identifier.ValueText, Normalize(method.TypeParameterList),
                        Normalize(method.ParameterList), Normalize(method.ConstraintClauses)),
                    Normalize(method.Body) + Normalize(method.ExpressionBody),
                    path);
                break;
            case ConstructorDeclarationSyntax constructor:
                yield return new MemberElement(
                    $"ctor:{Normalize(constructor.ParameterList)}",
                    Signature(constructor.AttributeLists, constructor.Modifiers,
                        constructor.Identifier.ValueText, Normalize(constructor.ParameterList)),
                    Normalize(constructor.Initializer) + Normalize(constructor.Body)
                        + Normalize(constructor.ExpressionBody),
                    path);
                break;
            case DestructorDeclarationSyntax destructor:
                yield return new MemberElement(
                    "destructor",
                    Signature(destructor.AttributeLists, destructor.Modifiers, destructor.Identifier.ValueText),
                    Normalize(destructor.Body) + Normalize(destructor.ExpressionBody),
                    path);
                break;
            case PropertyDeclarationSyntax property:
                yield return new MemberElement(
                    $"property:{Normalize(property.ExplicitInterfaceSpecifier)}{property.Identifier.ValueText}:{Normalize(property.Type)}",
                    Signature(property.AttributeLists, property.Modifiers,
                        Normalize(property.Type), Normalize(property.ExplicitInterfaceSpecifier),
                        property.Identifier.ValueText, AccessorSignature(property.AccessorList)),
                    AccessorBehavior(property.AccessorList) + Normalize(property.ExpressionBody)
                        + Normalize(property.Initializer),
                    path);
                break;
            case IndexerDeclarationSyntax indexer:
                yield return new MemberElement(
                    $"indexer:{Normalize(indexer.ExplicitInterfaceSpecifier)}:{Normalize(indexer.Type)}:{Normalize(indexer.ParameterList)}",
                    Signature(indexer.AttributeLists, indexer.Modifiers,
                        Normalize(indexer.Type), Normalize(indexer.ExplicitInterfaceSpecifier),
                        Normalize(indexer.ParameterList), AccessorSignature(indexer.AccessorList)),
                    AccessorBehavior(indexer.AccessorList) + Normalize(indexer.ExpressionBody),
                    path);
                break;
            case FieldDeclarationSyntax field:
                foreach (var variable in field.Declaration.Variables)
                {
                    yield return new MemberElement(
                        $"field:{variable.Identifier.ValueText}:{Normalize(field.Declaration.Type)}",
                        Signature(field.AttributeLists, field.Modifiers,
                            Normalize(field.Declaration.Type), variable.Identifier.ValueText),
                        Normalize(variable.Initializer),
                        path);
                }
                break;
            case EventFieldDeclarationSyntax eventField:
                foreach (var variable in eventField.Declaration.Variables)
                {
                    yield return new MemberElement(
                        $"event-field:{variable.Identifier.ValueText}:{Normalize(eventField.Declaration.Type)}",
                        Signature(eventField.AttributeLists, eventField.Modifiers,
                            Normalize(eventField.Declaration.Type), variable.Identifier.ValueText),
                        Normalize(variable.Initializer),
                        path);
                }
                break;
            case EventDeclarationSyntax eventDeclaration:
                yield return new MemberElement(
                    $"event:{Normalize(eventDeclaration.ExplicitInterfaceSpecifier)}{eventDeclaration.Identifier.ValueText}:{Normalize(eventDeclaration.Type)}",
                    Signature(eventDeclaration.AttributeLists, eventDeclaration.Modifiers,
                        Normalize(eventDeclaration.Type), Normalize(eventDeclaration.ExplicitInterfaceSpecifier),
                        eventDeclaration.Identifier.ValueText, AccessorSignature(eventDeclaration.AccessorList)),
                    AccessorBehavior(eventDeclaration.AccessorList),
                    path);
                break;
            case OperatorDeclarationSyntax operation:
                yield return new MemberElement(
                    $"operator:{operation.OperatorToken.Text}:{Normalize(operation.ParameterList)}:{Normalize(operation.ReturnType)}",
                    Signature(operation.AttributeLists, operation.Modifiers,
                        Normalize(operation.ReturnType), operation.OperatorToken.Text,
                        Normalize(operation.ParameterList)),
                    Normalize(operation.Body) + Normalize(operation.ExpressionBody),
                    path);
                break;
            case ConversionOperatorDeclarationSyntax conversion:
                yield return new MemberElement(
                    $"conversion:{conversion.ImplicitOrExplicitKeyword.Text}:{Normalize(conversion.Type)}:{Normalize(conversion.ParameterList)}",
                    Signature(conversion.AttributeLists, conversion.Modifiers,
                        conversion.ImplicitOrExplicitKeyword.Text, Normalize(conversion.Type),
                        Normalize(conversion.ParameterList)),
                    Normalize(conversion.Body) + Normalize(conversion.ExpressionBody),
                    path);
                break;
            case DelegateDeclarationSyntax delegateDeclaration:
                yield return new MemberElement(
                    $"delegate:{delegateDeclaration.Identifier.ValueText}:{Normalize(delegateDeclaration.ParameterList)}",
                    Normalize(delegateDeclaration),
                    string.Empty,
                    path);
                break;
        }
    }

    private static string TypeSignature(BaseTypeDeclarationSyntax declaration)
    {
        var typeParameters = declaration switch
        {
            TypeDeclarationSyntax type => Normalize(type.TypeParameterList),
            _ => string.Empty
        };
        var constraints = declaration switch
        {
            TypeDeclarationSyntax type => Normalize(type.ConstraintClauses),
            _ => string.Empty
        };
        var modifiers = declaration.Modifiers.Where(token => !token.IsKind(SyntaxKind.PartialKeyword));
        return Signature(
            declaration.AttributeLists,
            modifiers,
            TypeKind(declaration),
            declaration.Identifier.ValueText,
            typeParameters,
            Normalize(declaration.BaseList),
            constraints);
    }

    private static string FullTypeName(BaseTypeDeclarationSyntax declaration)
    {
        var namespaceName = string.Join(".", declaration.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>()
            .Reverse()
            .Select(item => item.Name.ToString()));
        var containingTypes = declaration.Ancestors()
            .OfType<BaseTypeDeclarationSyntax>()
            .Reverse()
            .Select(TypeName)
            .Append(TypeName(declaration));
        var typeName = string.Join("+", containingTypes);
        return string.IsNullOrEmpty(namespaceName) ? typeName : $"{namespaceName}.{typeName}";
    }

    private static string TypeName(BaseTypeDeclarationSyntax declaration)
    {
        var arity = declaration is TypeDeclarationSyntax type
            ? type.TypeParameterList?.Parameters.Count ?? 0
            : 0;
        return arity == 0 ? declaration.Identifier.ValueText : $"{declaration.Identifier.ValueText}`{arity}";
    }

    private static string TypeKind(BaseTypeDeclarationSyntax declaration) =>
        declaration switch
        {
            RecordDeclarationSyntax record => $"record-{record.ClassOrStructKeyword.Text.DefaultIfEmpty("class")}",
            EnumDeclarationSyntax => "enum",
            TypeDeclarationSyntax type => type.Keyword.Text,
            _ => declaration.Kind().ToString()
        };

    private static string ProjectName(string path) => path.Split('/')[2];

    private static string AccessorSignature(AccessorListSyntax? accessors) =>
        accessors is null
            ? string.Empty
            : string.Join(";", accessors.Accessors.Select(accessor =>
                Signature(accessor.AttributeLists, accessor.Modifiers, accessor.Keyword.Text)));

    private static string AccessorBehavior(AccessorListSyntax? accessors) =>
        accessors is null
            ? string.Empty
            : string.Join(";", accessors.Accessors.Select(accessor =>
                accessor.Keyword.Text + Normalize(accessor.Body) + Normalize(accessor.ExpressionBody)));

    private static string Signature(
        SyntaxList<AttributeListSyntax> attributes,
        IEnumerable<SyntaxToken> modifiers,
        params string[] parts) =>
        Normalize(attributes) + string.Concat(modifiers.Select(token => token.Text)) + string.Concat(parts);

    private static string Normalize(SyntaxNode? node) => node is null ? string.Empty : Normalize(node.DescendantTokens());

    private static string Normalize<T>(SyntaxList<T> nodes) where T : SyntaxNode =>
        string.Concat(nodes.Select(Normalize));

    private static string Normalize(SyntaxTokenList tokens) => Normalize(tokens.AsEnumerable());

    private static string Normalize(IEnumerable<SyntaxToken> tokens) => string.Concat(tokens.Select(token => token.Text));

    internal sealed record Snapshot(
        string Reference,
        IReadOnlyList<string> Files,
        IReadOnlyList<TypeElement> Types)
    {
        internal int FileCount => Files.Count;
        internal int MemberCount => Types.Sum(type => type.Members.Count);
    }

    internal sealed record TypeElement(
        string Key,
        string Project,
        string FullName,
        string Kind,
        string Signature,
        IReadOnlyList<string> Files,
        IReadOnlyList<MemberElement> Members);

    internal sealed record MemberElement(string Key, string Signature, string Behavior, string File);

    internal sealed record AddedMember(string Type, string Member, string File, string Signature);

    internal sealed record TypeDifference(
        string Id,
        IReadOnlyList<string> BaselineFiles,
        IReadOnlyList<string> TargetFiles,
        string BaselineSignature,
        string? TargetSignature);

    internal sealed record MemberDifference(
        string Id,
        string Type,
        string Member,
        string BaselineFile,
        string? TargetFile,
        string BaselineSignature,
        string? TargetSignature,
        string BaselineBehavior,
        string? TargetBehavior)
    {
        internal static MemberDifference Missing(string id, string type, MemberElement baseline) =>
            new(id, type, baseline.Key, baseline.File, null,
                baseline.Signature, null, baseline.Behavior, null);

        internal static MemberDifference Changed(
            string id,
            string type,
            MemberElement baseline,
            MemberElement target) =>
            new(id, type, baseline.Key, baseline.File, target.File,
                baseline.Signature, target.Signature, baseline.Behavior, target.Behavior);
    }

    internal sealed record Comparison(
        Snapshot Baseline,
        Snapshot Target,
        IReadOnlyDictionary<string, string> RelocatedTypes,
        int MatchedMembers,
        IReadOnlyList<TypeElement> AddedTypes,
        IReadOnlyList<AddedMember> AddedMembers,
        IReadOnlyList<TypeDifference> MissingTypes,
        IReadOnlyList<TypeDifference> TypeSignatureDifferences,
        IReadOnlyList<MemberDifference> MissingMembers,
        IReadOnlyList<MemberDifference> MemberSignatureDifferences,
        IReadOnlyList<MemberDifference> BodyDifferences);

    private sealed class TypeBuilder(
        string key,
        string project,
        string fullName,
        string kind,
        string signature,
        bool hasBaseList)
    {
        private string signature = signature;
        private bool hasBaseList = hasBaseList;

        internal HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        internal List<MemberElement> Members { get; } = [];

        internal void ConsiderSignature(string candidate, bool candidateHasBaseList)
        {
            if (!hasBaseList && candidateHasBaseList)
            {
                signature = candidate;
                hasBaseList = true;
            }
        }

        internal TypeElement Build() => new(
            key,
            project,
            fullName,
            kind,
            signature,
            Files.Order(StringComparer.Ordinal).ToArray(),
            Members.OrderBy(member => member.Key, StringComparer.Ordinal)
                .ThenBy(member => member.File, StringComparer.Ordinal)
                .ToArray());
    }

    private static string DefaultIfEmpty(this string value, string fallback) =>
        string.IsNullOrEmpty(value) ? fallback : value;
}
