using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using VerifyCS = DependencyInjection.SourceGenerator.Tests.CSharpSourceGeneratorVerifier<DependencyInjection.SourceGenerator.DependencyInjectionRegistrationGenerator>;
using Microsoft.CodeAnalysis.Testing;
using System.ComponentModel.Design;

namespace DependencyInjection.SourceGenerator.Tests;

public class DependencyInjectionRegistrationGeneratorTests
{
    private static Compilation CreateCompilation(string source)
            => CSharpCompilation.Create("DependencyInjection.SourceGenerator.Demo",
                new[] { CSharpSyntaxTree.ParseText(source) },
                new[] { MetadataReference.CreateFromFile(typeof(DependencyInjectionRegistrationGeneratorTests).GetTypeInfo().Assembly.Location) },
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));

    private readonly ImmutableArray<string> references = AppDomain.CurrentDomain
    .GetAssemblies()
    .Where(assembly => !assembly.IsDynamic)
    .Select(assembly => assembly.Location)
    .ToImmutableArray();

    private async Task RunTestAsync(string code, string expectedResult)
    {
        var tester = new VerifyCS.Test
        {
            TestState =
                {
                    Sources = { code },
                    GeneratedSources =
                    {
                        (typeof(DependencyInjectionRegistrationGenerator), "CompositionRoot.g.cs",
                            SourceText.From(expectedResult, Encoding.UTF8))
                    }
                },
            ReferenceAssemblies = ReferenceAssemblies.Net.Net60
        };

        tester.ReferenceAssemblies.AddAssemblies(references);
        tester.TestState.AdditionalReferences.Add(typeof(Contracts.Attributes.RegisterAttribute).Assembly);
        tester.TestState.AdditionalReferences.Add(typeof(LightInject.IServiceContainer).Assembly);
        
        await tester.RunAsync();
    }

    [Fact]
    public async Task CreateCompositionRoot_RegisterService_NoExistingCompositionRoot()
    {
        var code = """
using DependencyInjection.SourceGenerator.Contracts.Attributes;

namespace DependencyInjection.SourceGenerator.Demo;

[Register]
public class Service : IService {}
public interface IService {}

""";

        var expected = """
using LightInject;

namespace DependencyInjection.SourceGenerator.Demo;
public class CompositionRoot : ICompositionRoot
{
    public void Compose(IServiceRegistry serviceRegistry)
    {
        serviceRegistry.Register<DependencyInjection.SourceGenerator.Demo.IService, DependencyInjection.SourceGenerator.Demo.Service>(new PerRequestLifeTime());
    }
}
""";

        await RunTestAsync(code, expected);
        Assert.True(true); // silence warnings, real test happens in the RunAsync() method
    }

    [Fact]
    public async Task CreateCompositionRoot_RegisterService_ExistingCompositionRoot()
    {
        var code = """
using DependencyInjection.SourceGenerator.Contracts.Attributes;
using LightInject;

namespace DependencyInjection.SourceGenerator.Demo;

[Register]
public class Service : IService {}
public interface IService {}

public partial class CompositionRoot : ICompositionRoot
{
    public static void RegisterServices(IServiceRegistry serviceRegistry)
    {
        
    } 
}

""";

        var expected = """
using LightInject;

namespace DependencyInjection.SourceGenerator.Demo;
public partial class CompositionRoot : ICompositionRoot
{
    public void Compose(IServiceRegistry serviceRegistry)
    {
        RegisterServices(serviceRegistry);
        serviceRegistry.Register<DependencyInjection.SourceGenerator.Demo.IService, DependencyInjection.SourceGenerator.Demo.Service>(new PerRequestLifeTime());
    }
}
""";

        await RunTestAsync(code, expected);
        Assert.True(true); // silence warnings, real test happens in the RunAsync() method
    }

    [Fact]
    public async Task Register_SpecifiedLifetime_And_ServiceName()
    {
        var code = """
using DependencyInjection.SourceGenerator.Contracts.Attributes;
using DependencyInjection.SourceGenerator.Contracts.Enums;

namespace DependencyInjection.SourceGenerator.Demo;

[Register(Lifetime = Lifetime.Scoped, ServiceName = "Test")]
public class Service : IService {}
public interface IService {}

""";

        var expected = """
using LightInject;

namespace DependencyInjection.SourceGenerator.Demo;
public class CompositionRoot : ICompositionRoot
{
    public void Compose(IServiceRegistry serviceRegistry)
    {
        serviceRegistry.Register<DependencyInjection.SourceGenerator.Demo.IService, DependencyInjection.SourceGenerator.Demo.Service>("Test", new PerScopeLifetime());
    }
}
""";

        await RunTestAsync(code, expected);
        Assert.True(true); // silence warnings, real test happens in the RunAsync() method
    }
}