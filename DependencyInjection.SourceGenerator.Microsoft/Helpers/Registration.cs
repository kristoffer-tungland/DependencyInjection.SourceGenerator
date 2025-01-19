using DependencyInjection.SourceGenerator.Microsoft.Enums;

namespace DependencyInjection.SourceGenerator.Microsoft.Helpers;

internal sealed class Registration
{
    public required string? ServiceType { get; init; }
    public required string? ServiceName { get; init; }
    public required bool IncludeFactory { get; set; }
    public required ServiceLifetime Lifetime { get; init; }
    public required string ImplementationTypeName { get; init; }
}
