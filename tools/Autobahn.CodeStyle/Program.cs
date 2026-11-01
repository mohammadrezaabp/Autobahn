using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

var root = Path.GetFullPath(args.FirstOrDefault(x => !x.StartsWith('-')) ?? ".");
var split = args.Contains("--fix");
var extractNested = args.Contains("--extract-nested");
var references = new Dictionary<string, string>();
var violations = new List<string>();
var files = new[] { "src", "tests", "tools" }.SelectMany(dir => Directory.Exists(Path.Combine(root, dir))
    ? Directory.EnumerateFiles(Path.Combine(root, dir), "*.cs", SearchOption.AllDirectories) : [])
    .Where(file => !file.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj" or "node_modules")).ToArray();
foreach (var file in files)
{
    var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(file)).GetCompilationUnitRoot();
    if (extractNested)
    {
        while (syntax.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault(t => t.Modifiers.Any(SyntaxKind.PublicKeyword) && t.Parent is TypeDeclarationSyntax) is { } nested)
        {
            var parent = (TypeDeclarationSyntax)nested.Parent!;
            var oldName = nested.Identifier.Text;
            var newName = oldName switch { "Count" => "CallerStreamCount", "Candidate" => "BackendCandidate", _ => oldName };
            var containingNamespace = nested.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Single();
            var standalone = nested.WithIdentifier(SyntaxFactory.Identifier(newName));
            var unit = syntax.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(containingNamespace.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(standalone))));
            var destination = Path.Combine(Path.GetDirectoryName(file)!, newName + ".cs");
            if (File.Exists(destination)) throw new InvalidOperationException("Refusing to overwrite " + destination);
            File.WriteAllText(destination, unit.NormalizeWhitespace().ToFullString() + "\n");
            references[parent.Identifier.Text + "." + oldName] = newName;
            syntax = syntax.RemoveNode(nested, SyntaxRemoveOptions.KeepNoTrivia)!;
            if (newName != oldName)
                syntax = syntax.ReplaceTokens(syntax.DescendantTokens().Where(t => t.IsKind(SyntaxKind.IdentifierToken) && t.ValueText == oldName), (token, _) => SyntaxFactory.Identifier(token.LeadingTrivia, newName, token.TrailingTrivia));
        }
        File.WriteAllText(file, syntax.ToFullString());
    }
    foreach (var nested in syntax.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t => t.Modifiers.Any(SyntaxKind.PublicKeyword) && t.Parent is TypeDeclarationSyntax))
        violations.Add(Path.GetRelativePath(root, file) + ": nested public type " + nested.Identifier.Text + " must be extracted");
    var ns = syntax.Members.OfType<BaseNamespaceDeclarationSyntax>().SingleOrDefault();
    var members = ns?.Members ?? syntax.Members;
    var types = members.Where(m => m is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax).ToArray();
    if (types.Length == 0) continue;
    string Name(MemberDeclarationSyntax type) => type switch
    { BaseTypeDeclarationSyntax t => t.Identifier.Text, DelegateDeclarationSyntax d => d.Identifier.Text, _ => "" };
    var hasGlobals = members.OfType<GlobalStatementSyntax>().Any();
    var needsSplit = types.Length > 1 || (types.Length == 1 && Path.GetFileNameWithoutExtension(file) != Name(types[0]) && !Path.GetFileNameWithoutExtension(file).StartsWith(Name(types[0]) + ".", StringComparison.Ordinal));
    if (needsSplit)
    {
        violations.Add(Path.GetRelativePath(root, file));
        if (!split) continue;
        var writes = new List<(string File, string Text)>();
        foreach (var type in types)
        {
            var target = Path.Combine(Path.GetDirectoryName(file)!, Name(type) + ".cs");
            if (File.Exists(target) && target != file)
                target = Path.Combine(Path.GetDirectoryName(file)!, "Types", Path.GetFileNameWithoutExtension(file), Name(type) + ".cs");
            var body = ns is null ? syntax.WithMembers(SyntaxFactory.SingletonList(type))
                : syntax.WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(ns.WithMembers(SyntaxFactory.SingletonList(type))));
            writes.Add((target, body.NormalizeWhitespace().ToFullString() + "\n"));
        }
        if (hasGlobals)
            File.WriteAllText(file, syntax.WithMembers(SyntaxFactory.List(members.Except(types))).ToFullString());
        else File.Delete(file);
        foreach (var write in writes)
        { Directory.CreateDirectory(Path.GetDirectoryName(write.File)!); File.WriteAllText(write.File, write.Text); }
    }
}
if (extractNested)
    foreach (var file in files)
    {
        var content = File.ReadAllText(file);
        foreach (var (oldName, newName) in references) content = content.Replace(oldName, newName, StringComparison.Ordinal);
        File.WriteAllText(file, content);
    }
Console.WriteLine($"{(split ? "Fixed" : "Found")} {violations.Count} type/file layout violations across {files.Length} files.");
if (!split && violations.Count > 0) { foreach (var file in violations) Console.Error.WriteLine(file); return 1; }
return 0;
