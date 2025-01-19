using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.SourceGenerator.Microsoft.Helpers;

public static class ImplementationLookup
{
    public static IEnumerable<(INamedTypeSymbol implmentingType, INamedTypeSymbol serviceType)> GetImplementations(Compilation compilation, INamedTypeSymbol serviceTypeSymbol)
    {
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(syntaxTree);
            var root = syntaxTree.GetRoot();
            var typeDeclarations = root.DescendantNodes().OfType<TypeDeclarationSyntax>();

            foreach (var typeDeclaration in typeDeclarations)
            {
                var implementingType = semanticModel.GetDeclaredSymbol(typeDeclaration) as INamedTypeSymbol;
                if (implementingType is null)
                    continue;

                if (implementingType.IsAbstract)
                    continue;

                if (ImplementsInterface(implementingType, serviceTypeSymbol, out var serviceTypeFromInterface))
                {
                    yield return (implementingType, serviceTypeFromInterface);
                }

                if (IsDerivedFrom(implementingType, serviceTypeSymbol, out var serviceTypeFromBase))
                {
                    yield return (implementingType, serviceTypeFromBase);
                    continue;
                }
            }
        }
    }
    
    private static bool IsDerivedFrom(INamedTypeSymbol implementingType, INamedTypeSymbol serviceType, out INamedTypeSymbol implementedServiceType)
    {
        implementedServiceType = serviceType;
        var currentBaseType = implementingType.BaseType;
        while (currentBaseType != null)
        {
            if (SymbolEqualityComparer.Default.Equals(currentBaseType.OriginalDefinition, serviceType.OriginalDefinition))
            {
                implementedServiceType = currentBaseType;
                return true;
            }
            currentBaseType = currentBaseType.BaseType;
        }
        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol implementingType, INamedTypeSymbol serviceType, out INamedTypeSymbol implementedServiceType)
    {
        implementedServiceType = serviceType;
        
        foreach (var iface in implementingType.AllInterfaces)
        {
            if (iface.OriginalDefinition.Equals(serviceType.OriginalDefinition, SymbolEqualityComparer.Default))
            {
                implementedServiceType = iface;
                return true;
            }
        }
        return false;
    }
}
