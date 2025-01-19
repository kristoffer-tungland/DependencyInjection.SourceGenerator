using DependencyInjection.SourceGenerator.Microsoft.Enums;

namespace Microsoft.Extensions.DependencyInjection;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class RegisterAttribute : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public string? ServiceName { get; set; }
    public bool IncludeFactory { get; set; }
    public Type? ServiceType { get; set; }
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
internal class RegisterAttribute<TServiceType> : Attribute
{
    public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Transient;
    public string? ServiceName { get; set; }
    public bool IncludeFactory { get; set; }
}
