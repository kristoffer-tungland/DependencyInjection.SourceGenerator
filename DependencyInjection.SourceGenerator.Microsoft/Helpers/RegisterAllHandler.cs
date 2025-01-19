using System;
using CodeGenHelpers.Internals;
using DependencyInjection.SourceGenerator.Microsoft.Enums;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;

namespace DependencyInjection.SourceGenerator.Microsoft.Helpers;

public static class RegisterAllHandler
{
    public static void Process(Compilation compilation, List<ExpressionStatementSyntax> bodyMembers)
    {
        var registerAllAttributes = compilation.Assembly.GetAttributes().Where(a => a.AttributeClass?.Name == nameof(RegisterAllAttribute)).ToArray();
        
        foreach (var attribute in registerAllAttributes)
        {
            var serviceType = TypeHelper.GetServiceTypeFromAttribute(attribute);
            if (serviceType is null)
                continue;

            var lifetime = TypeHelper.GetLifetimeFromAttribute(attribute) ?? ServiceLifetime.Transient;
            var includeServiceName = TypeHelper.GetAttributeValue(attribute, nameof(RegisterAllAttribute.IncludeServiceName)) as bool? ?? false;
            var implementations = ImplementationLookup.GetImplementations(compilation, serviceType);
            foreach (var (implementationType, actualServiceType) in implementations)
            {
                var serviceName = includeServiceName ? implementationType.Name : null;
                bodyMembers.Add(RegistrationMapper.CreateRegistrationSyntax(actualServiceType.ToDisplayString(), implementationType.ToDisplayString(), lifetime, serviceName));
            }
        }
    }
}
