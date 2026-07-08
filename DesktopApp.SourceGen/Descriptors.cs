using Microsoft.CodeAnalysis;

namespace DesktopApp.SourceGen;

/// <summary>
///   Diagnostic descriptors reported by <see cref="ViewMappingGenerator"/>.
/// </summary>
internal static class Descriptors
{
    public static readonly DiagnosticDescriptor ViewNotFound = new(
        id: "DSG001",
        title: "View type not found",
        messageFormat: "Could not resolve View for ViewModel '{0}', expected type '{1}' in namespace '{2}'",
        category: "DesktopApp.SourceGen",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ViewNoParameterlessConstructor = new(
        id: "DSG002",
        title: "View has no accessible parameterless constructor",
        messageFormat: "View type '{0}' must declare a public parameterless constructor so ViewLocator can instantiate it",
        category: "DesktopApp.SourceGen",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
