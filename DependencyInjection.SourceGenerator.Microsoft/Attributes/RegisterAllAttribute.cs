using DependencyInjection.SourceGenerator.Microsoft.Enums;

namespace Microsoft.Extensions.DependencyInjection;

public interface IRegisterAllAttribute
{
    ServiceLifetime Lifetime { get;}
    bool IncludeServiceName { get; } 
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal class RegisterAllAttribute(Type serviceType) : Attribute, IRegisterAllAttribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public Type ServiceType { get; set; } = serviceType;

    public bool IncludeServiceName { get; set; }

    public RegisterAllAttribute(Type serviceType, ServiceLifetime lifetime) : this(serviceType)
    {
        Lifetime = lifetime;
    }
}


[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
internal class RegisterAllAttribute<TServiceType> : Attribute, IRegisterAllAttribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public bool IncludeServiceName { get; set; }
}
