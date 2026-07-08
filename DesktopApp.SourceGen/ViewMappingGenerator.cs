using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;

namespace DesktopApp.SourceGen;

/// <summary>
///   Scans the current compilation for classes that derive from
///   <c>DesktopApp.ViewModels.ViewModelBase</c> and pairs each one with the
///   View whose name follows the convention <c>{Xxx}ViewModel</c> -&gt;
///   <c>{Xxx}View</c>.  The result is emitted as a static, trimming-friendly
///   switch expression so AOT/trimming can see every View reference.
/// </summary>
[Generator]
public sealed class ViewMappingGenerator : IIncrementalGenerator
{
    private const string ViewModelBaseFullName = "DesktopApp.ViewModels.ViewModelBase";
    private const string GeneratedNamespace = "DesktopApp.Views";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var typeDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => (ClassDeclarationSyntax)ctx.Node);

        var combined = typeDeclarations.Collect().Combine(context.CompilationProvider);

        context.RegisterSourceOutput(combined, static (spc, source) =>
        {
            var (classes, comp) = source;
            Execute(spc, comp, classes);
        });
    }

    private static void Execute(
        SourceProductionContext spc,
        Compilation compilation,
        System.Collections.Immutable.ImmutableArray<ClassDeclarationSyntax> classes)
    {
        var viewModelBase = compilation.GetTypeByMetadataName(ViewModelBaseFullName);
        if (viewModelBase is null)
        {
            return;
        }

        var collected = new System.Collections.Generic.List<Mapping>();
        var visited = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        foreach (var decl in classes)
        {
            if (decl.Identifier.ValueText is not { Length: > 0 } name)
            {
                continue;
            }

            var symbol = compilation.GetSemanticModel(decl.SyntaxTree).GetDeclaredSymbol(decl);
            if (symbol is not INamedTypeSymbol namedType)
            {
                continue;
            }

            if (!InheritsFrom(namedType, viewModelBase))
            {
                continue;
            }

            if (symbol.IsAbstract)
            {
                continue;
            }

            if (!visited.Add(symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
            {
                continue;
            }

            if (!name.EndsWith("ViewModel", StringComparison.Ordinal))
            {
                continue;
            }

            var viewName = name.Substring(0, name.Length - "ViewModel".Length) + "View";

            var viewType = TryResolveViewType(compilation, viewName);
            if (viewType is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ViewNotFound,
                    symbol.Locations.Length > 0 ? symbol.Locations[0] : Location.None,
                    name,
                    viewName,
                    GeneratedNamespace));
                continue;
            }

            if (!HasAccessibleParameterlessConstructor(viewType))
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    Descriptors.ViewNoParameterlessConstructor,
                    viewType.Locations.Length > 0 ? viewType.Locations[0] : Location.None,
                    viewType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)));
                continue;
            }

            collected.Add(new Mapping(namedType, viewType));
        }

        spc.AddSource("ViewMapping.g.cs", Emit(collected));
    }

    private static INamedTypeSymbol? TryResolveViewType(Compilation compilation, string simpleName)
    {
        var desktopAppNs = FindNamespace(compilation.GlobalNamespace, "DesktopApp");
        if (desktopAppNs is not null)
        {
            var viewsNs = FindNamespace(desktopAppNs, "Views");
            if (viewsNs is not null)
            {
                foreach (var type in viewsNs.GetTypeMembers())
                {
                    if (type.Name == simpleName && type.TypeKind == TypeKind.Class)
                    {
                        return type;
                    }
                }
            }
        }

        // Fallback: any top-level type with the right simple name.
        foreach (var type in compilation.Assembly.GlobalNamespace.GetTypeMembers())
        {
            if (type.Name == simpleName && type.TypeKind == TypeKind.Class)
            {
                return type;
            }
        }

        return null;
    }

    private static INamespaceSymbol? FindNamespace(INamespaceSymbol parent, string name)
    {
        foreach (var member in parent.GetNamespaceMembers())
        {
            if (member is INamespaceSymbol ns && ns.Name == name)
            {
                return ns;
            }
        }
        return null;
    }

    private static bool InheritsFrom(INamedTypeSymbol symbol, INamedTypeSymbol baseType)
    {
        var current = symbol.BaseType;
        while (current is not null)
        {
            if (SymbolEqualityComparer.Default.Equals(current, baseType))
            {
                return true;
            }
            current = current.BaseType;
        }
        return false;
    }

    private static bool HasAccessibleParameterlessConstructor(INamedTypeSymbol type)
    {
        if (type.IsAbstract)
        {
            return false;
        }

        foreach (var ctor in type.Constructors)
        {
            if (ctor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }
            if (ctor.Parameters.Length == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static string Emit(System.Collections.Generic.IReadOnlyList<Mapping> mappings)
    {
        var sb = new System.Text.StringBuilder();
        _ = sb.AppendLine("// <auto-generated/>");
        _ = sb.AppendLine("#nullable enable");
        _ = sb.AppendLine("using Avalonia.Controls;");
        _ = sb.AppendLine("using DesktopApp.ViewModels;");
        _ = sb.AppendLine();
        _ = sb.AppendLine($"namespace {GeneratedNamespace}");
        _ = sb.AppendLine("{");
        _ = sb.AppendLine("    internal static class ViewMapping");
        _ = sb.AppendLine("    {");
        _ = sb.AppendLine("        /// <summary>");
        _ = sb.AppendLine("        ///   Resolves the View instance for the supplied ViewModel.");
        _ = sb.AppendLine("        /// </summary>");
        _ = sb.AppendLine("        public static Control? ResolveView(ViewModelBase viewModel)");
        _ = sb.AppendLine("        {");
        if (mappings.Count == 0)
        {
            _ = sb.AppendLine("            _ = viewModel;");
            _ = sb.AppendLine("            return null;");
        }
        else
        {
            _ = sb.AppendLine("            return viewModel switch");
            _ = sb.AppendLine("            {");
            foreach (var m in mappings)
            {
                var vmRef = m.ViewModel.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var viewRef = m.View.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                _ = sb.AppendLine($"                {vmRef} => new {viewRef}(),");
            }
            _ = sb.AppendLine("                _ => null");
            _ = sb.AppendLine("            };");
        }
        _ = sb.AppendLine("        }");
        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    private readonly struct Mapping(INamedTypeSymbol viewModel, INamedTypeSymbol view)
    {
        public readonly INamedTypeSymbol ViewModel = viewModel;
        public readonly INamedTypeSymbol View = view;
    }
}
