using DependencyInjection.SourceGenerator.Microsoft.Enums;

namespace Microsoft.Extensions.DependencyInjection;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal class RegisterAllAttribute(Type serviceType) : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public Type ServiceType { get; set; } = serviceType;

    public bool IncludeServiceName { get; set; }
    public bool IncludeFactory { get; set; }

    public RegisterAllAttribute(Type serviceType, ServiceLifetime lifetime) : this(serviceType)
    {
        Lifetime = lifetime;
    }
}


[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal class RegisterAllAttribute<TServiceType> : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public bool IncludeServiceName { get; set; }
    public bool IncludeFactory { get; set; }
}
