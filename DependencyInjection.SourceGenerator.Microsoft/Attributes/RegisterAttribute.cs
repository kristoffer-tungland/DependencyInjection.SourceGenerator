using DependencyInjection.SourceGenerator.Microsoft.Enums;

namespace DependencyInjection.SourceGenerator.Contracts.Attributes;
public interface IRegisterAttribute
{
    public ServiceLifetime Lifetime { get; }
    public string? ServiceName { get; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class RegisterAttribute : Attribute, IRegisterAttribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public string? ServiceName { get; set; }
    public Type? ServiceType { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class RegisterAttribute<TServiceType> : Attribute, IRegisterAttribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public string? ServiceName { get; set; }
}
