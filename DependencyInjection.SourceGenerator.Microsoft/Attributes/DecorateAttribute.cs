using System;

namespace Microsoft.Extensions.DependencyInjection;

[AttributeUsage(AttributeTargets.Class)]
internal class DecorateAttribute : Attribute
{
    public Type? ServiceType { get; set; }
}

[AttributeUsage(AttributeTargets.Class)]
internal class DecorateAttribute<TServiceType> : Attribute
{
}