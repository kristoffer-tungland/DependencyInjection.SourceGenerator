using DependencyInjection.SourceGenerator.Contracts.Enums;
using Microsoft.CodeAnalysis;
using System;

namespace DependencyInjection.SourceGenerator.Shared;

internal sealed class Registration
{
    public required string? ServiceType { get; init; }
    public required string? ServiceName { get; init; }
    public required Lifetime Lifetime { get; init; }
    public required string ImplementationTypeName { get; init; }
    public required INamedTypeSymbol ImplementationType { get; init; }
    public ServiceType? ServiceTypeMetadata { get; init; }
}
