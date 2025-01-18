using System;

namespace DependencyInjection.SourceGenerator.Contracts.Attributes;

[AttributeUsage(AttributeTargets.Class)]
internal class DecorateAttribute : Attribute
{
    public Type? ServiceType { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
internal class DecorateAttribute<TServiceType> : Attribute
{
}