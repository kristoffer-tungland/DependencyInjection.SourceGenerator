; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 3.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI001 | DependencyInjection | Error | The method '{0}' used for registering services must be static
DI002 | DependencyInjection | Error | The method '{0}' used for registering services must be public or internal
DI003 | DependencyInjection | Error | The method '{0}' used for registering services must have exactly one parameter of type System.IServiceProvider
DI004 | DependencyInjection | Error | The method '{0}' used for registering services cannot return void
DI005 | DependencyInjection | Error | The method '{0}' used for registering services must have exactly one parameter of type System.IServiceProvider or Microsoft.Extensions.DependencyInjection.IServiceCollection

