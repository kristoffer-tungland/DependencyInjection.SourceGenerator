using System;

namespace DependencyInjection.SourceGenerator.Contracts.Attributes;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FactoryArgumentAttribute : Attribute
{
}
