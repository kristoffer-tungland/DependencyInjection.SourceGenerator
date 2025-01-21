using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace DependencyInjection.SourceGenerator.Microsoft.Helpers;
internal static class DecorationMapper
{
    internal static List<Decoration> CreateDecoration(INamedTypeSymbol type)
    {
        var attributes = TypeHelper.GetClassAttributes<DecorateAttribute>(type);

        if (attributes is null || attributes.Count == 0)
            return [];

        var result = new List<Decoration>();
        foreach (var attribute in attributes)
        {
            var serviceType = TypeHelper.GetServiceType(type, attribute);
            if (serviceType is null)
                return [];

            var decoration = new Decoration
            {
                DecoratorTypeName = TypeHelper.GetFullName(type),
                DecoratedTypeName = serviceType.Name
            };

            result.Add(decoration);
        }

        return result;
    }
}
