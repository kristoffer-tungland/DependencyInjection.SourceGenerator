using Microsoft.CodeAnalysis;

namespace DependencyInjection.SourceGenerator.Microsoft.Diagnostics;

public static class MethodRegistrationDiagnostics
{
    private static readonly DiagnosticDescriptor MustBeStatic = new(
        "DI001",
        "Method Registration",
        "Register method '{0}' must be static",
        "DependencyInjection",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MustBePublicOrInternal = new(
        "DI002",
        "Method Registration",
        "Register method '{0}' must be public or internal",
        "DependencyInjection",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidParameters = new(
        "DI003",
        "Method Registration",
        "Register method '{0}' must have exactly one parameter of type System.IServiceProvider",
        "DependencyInjection",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CannotReturnVoid = new(
        "DI004",
        "Method Registration",
        "Register method '{0}' cannot return void",
        "DependencyInjection",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MustHaveIServiceCollectionParameter = new(
        "DI005",
        "Method Registration",
        "Register method '{0}' must have exactly one parameter of type System.IServiceProvider or Microsoft.Extensions.DependencyInjection.IServiceCollection",
        "DependencyInjection",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static void ReportMustBeStatic(IMethodSymbol methodSymbol, SourceProductionContext context)
    {
        var diagnostic = Diagnostic.Create(MustBeStatic, Location.None, methodSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }

    public static void ReportMustBePublicOrInternal(IMethodSymbol methodSymbol, SourceProductionContext context)
    {
        var diagnostic = Diagnostic.Create(MustBePublicOrInternal, Location.None, methodSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }

    public static void ReportInvalidParameters(IMethodSymbol methodSymbol, SourceProductionContext context)
    {
        var diagnostic = Diagnostic.Create(InvalidParameters, Location.None, methodSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }

    public static void ReportCannotReturnVoid(IMethodSymbol methodSymbol, SourceProductionContext context)
    {
        var diagnostic = Diagnostic.Create(CannotReturnVoid, Location.None, methodSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }

    public static void ReportInvalidMethodParameter(IMethodSymbol methodSymbol, SourceProductionContext context)
    {
        var diagnostic = Diagnostic.Create(MustHaveIServiceCollectionParameter, Location.None, methodSymbol.Name);
        context.ReportDiagnostic(diagnostic);
    }
}