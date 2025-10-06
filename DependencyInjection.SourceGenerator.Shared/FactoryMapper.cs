using DependencyInjection.SourceGenerator.Contracts.Attributes;
using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Linq;
using System;

namespace DependencyInjection.SourceGenerator.Shared;

internal static class FactoryMapper
{
    internal static FactoryDefinition? CreateFactory(INamedTypeSymbol type, IReadOnlyList<Registration> registrations, string fallbackNamespace)
    {
        var constructors = type.InstanceConstructors
            .Where(static ctor => !ctor.IsImplicitlyDeclared)
            .Select(ctor => new
            {
                Constructor = ctor,
                FactoryParameters = ctor.Parameters
                    .Where(parameter => TypeHelper.GetAttributes<FactoryArgumentAttribute>(parameter.GetAttributes()).Any())
                    .ToArray()
            })
            .Where(x => x.FactoryParameters.Length > 0)
            .OrderByDescending(x => x.FactoryParameters.Length)
            .ThenByDescending(x => x.Constructor.Parameters.Length)
            .FirstOrDefault();

        if (constructors is null)
            return null;

        var registration = registrations.FirstOrDefault();
        if (registration is null)
            return null;

        var namespaceName = type.ContainingNamespace is { IsGlobalNamespace: false }
            ? type.ContainingNamespace.ToDisplayString()
            : fallbackNamespace;

        if (string.IsNullOrWhiteSpace(namespaceName))
            return null;

        var serviceBaseName = registration.ServiceTypeMetadata?.Type.Name ?? type.Name;
        var interfaceName = serviceBaseName.StartsWith("I", StringComparison.Ordinal)
            ? serviceBaseName + "Factory"
            : "I" + serviceBaseName + "Factory";
        var implementationName = type.Name + "Factory";

        var returnType = registration.ServiceType ?? registration.ImplementationTypeName;

        return new FactoryDefinition
        {
            InterfaceName = interfaceName,
            ImplementationName = implementationName,
            Namespace = namespaceName,
            ReturnType = returnType,
            ImplementationTypeName = registration.ImplementationTypeName,
            Parameters = constructors.FactoryParameters,
            ServiceName = registration.ServiceName,
            ServiceType = registration.ServiceType,
        };
    }
}

internal sealed class FactoryDefinition
{
    public required string InterfaceName { get; init; }
    public required string ImplementationName { get; init; }
    public required string Namespace { get; init; }
    public required string ReturnType { get; init; }
    public required string ImplementationTypeName { get; init; }
    public required IReadOnlyList<IParameterSymbol> Parameters { get; init; }
    public string? ServiceName { get; init; }
    public string? ServiceType { get; init; }

    public string InterfaceFullName => $"global::{Namespace}.{InterfaceName}";
    public string ImplementationFullName => $"global::{Namespace}.{ImplementationName}";
}
