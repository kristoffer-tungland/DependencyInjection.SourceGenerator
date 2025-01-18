namespace DependencyInjection.SourceGenerator.Microsoft.Helpers;

public static class AttributeSourceTexts
{
    public const string RegisterAttributeText = @"

namespace global::Microsoft.Extensions.DependencyInjection
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true)]
    internal class RegisterAttribute : global::System.Attribute
    {
        public global::Microsoft.Extensions.DependencyInjection.ServiceLifetime Lifetime { get; set; } = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;
        public string? ServiceName { get; set; }
        public global::System.Type? ServiceType { get; set; }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Class, AllowMultiple = true)]
    internal class RegisterAttribute<TServiceType> : global::System.Attribute
    {
        public global::Microsoft.Extensions.DependencyInjection.ServiceLifetime Lifetime { get; set; } = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;
        public string? ServiceName { get; set; }
    }
}";

    public const string RegisterAllAttributeText = @"

namespace global::Microsoft.Extensions.DependencyInjection
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
    internal class RegisterAllAttribute : global::System.Attribute
    {
        public global::Microsoft.Extensions.DependencyInjection.ServiceLifetime Lifetime { get; set; } = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;
        public global::System.Type ServiceType { get; set; }
        public bool IncludeServiceName { get; set; }

        public RegisterAllAttribute(global::System.Type serviceType)
        {
            ServiceType = serviceType;
        }

        public RegisterAllAttribute(global::System.Type serviceType, global::Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime) : this(serviceType)
        {
            Lifetime = lifetime;
        }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Assembly, AllowMultiple = true)]
    internal class RegisterAllAttribute<TServiceType> : global::System.Attribute
    {
        public global::Microsoft.Extensions.DependencyInjection.ServiceLifetime Lifetime { get; set; } = global::Microsoft.Extensions.DependencyInjection.ServiceLifetime.Transient;
        public bool IncludeServiceName { get; set; }
    }
}";

    public const string DecorateAttributeText = @"

namespace global::Microsoft.Extensions.DependencyInjection
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
    internal class DecorateAttribute : global::System.Attribute
    {
        public global::System.Type? ServiceType { get; set; }
    }

    [global::System.AttributeUsage(global::System.AttributeTargets.Class)]
    internal class DecorateAttribute<TServiceType> : global::System.Attribute
    {
    }
}";
}
