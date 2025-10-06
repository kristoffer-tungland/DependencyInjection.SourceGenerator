using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using DependencyInjection.SourceGenerator.Contracts.Enums;
using DependencyInjection.SourceGenerator.Shared;
using DependencyInjection.SourceGenerator.Microsoft.Contracts.Attributes;

namespace DependencyInjection.SourceGenerator.Microsoft;

public record RegistrationExtension(string ClassFullName, string MethodName, List<Diagnostic> Errors);

[Generator]
public class DependencyInjectionRegistrationGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ClassAttributeReceiver(additionalMethodAttributes: [nameof(RegistrationExtensionAttribute)]));
    }

    public void Execute(GeneratorExecutionContext context)
    {
        var @namespace = "Microsoft.Extensions.DependencyInjection";
        var safeAssemblyName = EscapeAssemblyNameToMethodName(context.Compilation.AssemblyName);
        var extensionName = "Add" + safeAssemblyName;

        var classesToRegister = RegistrationCollector.GetTypes(context);
        var registerAllTypes = RegistrationCollector.GetRegisterAllTypes(context);
        var defaultNamespace = GetDefaultNamespace(context);
        var factoryDefinitions = new List<FactoryDefinition>();

        var source = GenerateExtensionMethod(context, extensionName, @namespace, classesToRegister, registerAllTypes, defaultNamespace, factoryDefinitions);
        var sourceText = source.ToFullString();
        context.AddSource("ServiceCollectionExtensions.g.cs", SourceText.From(sourceText, Encoding.UTF8));

        foreach (var factory in factoryDefinitions)
        {
            var factorySource = GenerateFactory(factory);
            context.AddSource($"{factory.ImplementationName}.Factory.g.cs", SourceText.From(factorySource.ToFullString(), Encoding.UTF8));
        }
    }

    public static string EscapeAssemblyNameToMethodName(string? assemblyName)
    {
        if (string.IsNullOrWhiteSpace(assemblyName))
            return "Default";

        var sb = new StringBuilder();
        var ensureNextUpper = true;
        foreach (var c in assemblyName!)
        {
            if (char.IsLetterOrDigit(c))
            {
                var letter = c;
                if (ensureNextUpper)
                {
                    letter = char.ToUpperInvariant(c);
                    ensureNextUpper = false;
                }
                sb.Append(letter);
            }
            else
            {
                ensureNextUpper = true;
                continue;
            }
        }
        return sb.ToString();
    }

    private static string GetDefaultNamespace(GeneratorExecutionContext context)
    {
        var @namespace = context.Compilation.SyntaxTrees
        .SelectMany(x => x.GetRoot().DescendantNodes())
        .OfType<NamespaceDeclarationSyntax>()
        .Select(x => x.Name.ToString())
        .Min();

        if (@namespace is not null)
            return @namespace;

        @namespace = context.Compilation.SyntaxTrees
        .SelectMany(x => x.GetRoot().DescendantNodes())
        .OfType<FileScopedNamespaceDeclarationSyntax>()
        .Select(x => x.Name.ToString())
        .Min();

        if (@namespace is not null)
            return @namespace;

        throw new NotSupportedException("Unable to calculate namespace");
    }

    private static CompilationUnitSyntax GenerateExtensionMethod(GeneratorExecutionContext context, string extensionName, string @namespace, IEnumerable<INamedTypeSymbol> classesToRegister, IEnumerable<Registration> additionalRegistrations, string fallbackNamespace, List<FactoryDefinition> factoryDefinitions)
    {
        var bodyMembers = new List<ExpressionStatementSyntax>();

        foreach (var type in classesToRegister)
        {
            var registrations = RegistrationMapper.CreateRegistration(type);
            foreach (var registration in registrations)
            {
                bodyMembers.Add(CreateRegistrationSyntax(registration.ServiceType, registration.ImplementationTypeName, registration.Lifetime, registration.ServiceName));
            }

            var decoration = DecorationMapper.CreateDecoration(type);
            if (decoration is not null)
                bodyMembers.Add(CreateDecorationSyntax(decoration.DecoratedTypeName, decoration.DecoratorTypeName));

            var registrationExtensions = CreateRegistrationExtensions(type);

            foreach (var registrationExtension in registrationExtensions)
            {
                if (!registrationExtension.Errors.Any())
                {
                    bodyMembers.Add(CreateRegistrationExtensionSyntax(registrationExtension.ClassFullName, registrationExtension.MethodName));
                    continue;
                }
                foreach (var error in registrationExtension.Errors)
                {
                    context.ReportDiagnostic(error);
                }
            }

            var factory = FactoryMapper.CreateFactory(type, registrations, fallbackNamespace);
            if (factory is not null)
            {
                factoryDefinitions.Add(factory);
                bodyMembers.Add(CreateRegistrationSyntax(factory.InterfaceFullName, factory.ImplementationFullName, Lifetime.Singleton, null));
            }
        }

        foreach (var registration in additionalRegistrations)
        {
            bodyMembers.Add(CreateRegistrationSyntax(registration.ServiceType, registration.ImplementationTypeName, registration.Lifetime, registration.ServiceName));
        }

        var methodModifiers = SyntaxFactory.TokenList([SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword)]);

        var serviceCollectionSyntax = SyntaxFactory.QualifiedName(
                            SyntaxFactory.QualifiedName(
                                SyntaxFactory.QualifiedName(
                                    SyntaxFactory.AliasQualifiedName(
                                        SyntaxFactory.IdentifierName(
                                            SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                                        SyntaxFactory.IdentifierName("Microsoft")),
                                    SyntaxFactory.IdentifierName("Extensions")),
                                SyntaxFactory.IdentifierName("DependencyInjection")),
                            SyntaxFactory.IdentifierName("IServiceCollection"));

        var methodDeclaration = SyntaxFactory.MethodDeclaration(serviceCollectionSyntax, SyntaxFactory.Identifier(extensionName))
                            .WithModifiers(SyntaxFactory.TokenList(methodModifiers))
                            .WithParameterList(
                                SyntaxFactory.ParameterList(
                                    SyntaxFactory.SingletonSeparatedList<ParameterSyntax>(
                                        SyntaxFactory.Parameter(
                                            SyntaxFactory.Identifier("services"))
                                        .WithModifiers(
                                            SyntaxFactory.TokenList(
                                                SyntaxFactory.Token(SyntaxKind.ThisKeyword)))
                                        .WithType(serviceCollectionSyntax))));


        var body = SyntaxFactory.Block(bodyMembers.ToArray());
        var returnStatement = SyntaxFactory.ReturnStatement(SyntaxFactory.IdentifierName("services"));
        body = body.AddStatements(returnStatement);

        methodDeclaration = methodDeclaration.WithBody(body);

        var classModifiers = SyntaxFactory.TokenList([SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword), SyntaxFactory.Token(SyntaxKind.PartialKeyword)]);
        var classDeclaration = SyntaxFactory.ClassDeclaration("ServiceCollectionExtensions")
                    .WithModifiers(classModifiers)
                    .WithMembers(SyntaxFactory.SingletonList<MemberDeclarationSyntax>(methodDeclaration));

        var dependencyInjectionUsingDirective = SyntaxFactory.UsingDirective(
            SyntaxFactory.QualifiedName(
            SyntaxFactory.QualifiedName(
                SyntaxFactory.AliasQualifiedName(
                    SyntaxFactory.IdentifierName(
                        SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                    SyntaxFactory.IdentifierName("Microsoft")),
                SyntaxFactory.IdentifierName("Extensions")),
            SyntaxFactory.IdentifierName("DependencyInjection")));

        return Trivia.CreateCompilationUnitSyntax(classDeclaration, @namespace, [dependencyInjectionUsingDirective]);
    }

    private static CompilationUnitSyntax GenerateFactory(FactoryDefinition factory)
    {
        var interfaceDeclaration = SyntaxFactory.InterfaceDeclaration(factory.InterfaceName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithMembers(
                SyntaxFactory.SingletonList<MemberDeclarationSyntax>(
                    SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(factory.ReturnType), SyntaxFactory.Identifier("Create"))
                        .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(factory.Parameters.Select(CreateParameter))))
                        .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))));

        var serviceProviderType = SyntaxFactory.ParseTypeName("global::System.IServiceProvider");
        var fieldDeclaration = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(serviceProviderType)
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(CreateIdentifier("_serviceProvider")))))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

        var constructorDeclaration = SyntaxFactory.ConstructorDeclaration(SyntaxFactory.Identifier(factory.ImplementationName))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Parameter(CreateIdentifier("serviceProvider"))
                            .WithType(serviceProviderType))))
            .WithBody(
                SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(CreateIdentifier("_serviceProvider")),
                            SyntaxFactory.IdentifierName(CreateIdentifier("serviceProvider"))))));

        var createMethod = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(factory.ReturnType), SyntaxFactory.Identifier("Create"))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(factory.Parameters.Select(CreateParameter))))
            .WithBody(
                SyntaxFactory.Block(
                    SyntaxFactory.ReturnStatement(
                        SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("global::Microsoft.Extensions.DependencyInjection.ActivatorUtilities"),
                                SyntaxFactory.GenericName(SyntaxFactory.Identifier("CreateInstance"))
                                    .WithTypeArgumentList(
                                        SyntaxFactory.TypeArgumentList(
                                            SyntaxFactory.SingletonSeparatedList<TypeSyntax>(SyntaxFactory.ParseTypeName(factory.ImplementationTypeName))))))
                        .WithArgumentList(
                            SyntaxFactory.ArgumentList(
                                SyntaxFactory.SeparatedList(CreateActivatorArguments(factory.Parameters)))))));

        var classDeclaration = SyntaxFactory.ClassDeclaration(factory.ImplementationName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(factory.InterfaceFullName)))
            .AddMembers(fieldDeclaration, constructorDeclaration, createMethod);

        return Trivia.CreateCompilationUnitSyntax(new MemberDeclarationSyntax[] { interfaceDeclaration, classDeclaration }, factory.Namespace);
    }

    private static IEnumerable<ArgumentSyntax> CreateActivatorArguments(IReadOnlyList<IParameterSymbol> parameters)
    {
        var arguments = new List<ArgumentSyntax>
        {
            SyntaxFactory.Argument(SyntaxFactory.IdentifierName(CreateIdentifier("_serviceProvider")))
        };

        foreach (var parameter in parameters)
        {
            arguments.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(CreateIdentifier(parameter.Name))));
        }

        return arguments;
    }

    private static ParameterSyntax CreateParameter(IParameterSymbol parameter)
    {
        return SyntaxFactory.Parameter(CreateIdentifier(parameter.Name))
            .WithType(SyntaxFactory.ParseTypeName(TypeHelper.GetFullName(parameter.Type)));
    }

    private static SyntaxToken CreateIdentifier(string name)
    {
        return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.Identifier("@" + name);
    }

    internal static List<RegistrationExtension> CreateRegistrationExtensions(INamedTypeSymbol type)
    {
        var registrations = new List<RegistrationExtension>();
        foreach (var member in type.GetMembers())
        {
            if (member is not IMethodSymbol method)
                continue;

            var attribute = TypeHelper.GetAttributes<RegistrationExtensionAttribute>(member.GetAttributes());
            if (!attribute.Any())
                continue;

            List<Diagnostic> errors = [];
            if (method.DeclaredAccessibility is not Accessibility.Public and not Accessibility.Internal and not Accessibility.Friend)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "DIM0001",
                        "Invalid method accessor",
                        "Method {0} on type {1} must be public or internal",
                        "InvalidConfig",
                        DiagnosticSeverity.Error,
                        true), null, method.Name, type.Name);
                errors.Add(diagnostic);
            }

            if (!method.IsStatic)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "DIM0002",
                        "Method must be static",
                        "Method {0} on type {1} must be static",
                        "InvalidConfig",
                        DiagnosticSeverity.Error,
                        true), null, method.Name, type.Name);
                errors.Add(diagnostic);
            }

            if (method.Parameters.Length != 1)
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "DIM0002",
                        "Invalid parameter count",
                        "Method {0} on type {1} must have exactly one parameter of type IServiceCollection",
                        "InvalidConfig",
                        DiagnosticSeverity.Error,
                        true), null, method.Name, type.Name);
                errors.Add(diagnostic);
            }

            var firstParameter = method.Parameters.FirstOrDefault();
            if (firstParameter is not null && TypeHelper.GetFullName(firstParameter.Type) != "global::Microsoft.Extensions.DependencyInjection.IServiceCollection")
            {
                var diagnostic = Diagnostic.Create(
                    new DiagnosticDescriptor(
                        "DIM0002",
                        "Invalid parameter type",
                        "Method {0} on type {1} must have input parameter of type IServiceCollection",
                        "InvalidConfig",
                        DiagnosticSeverity.Error,
                        true), null, method.Name, type.Name);
                errors.Add(diagnostic);
            }

            var registration = new RegistrationExtension(TypeHelper.GetFullName(type), method.Name, errors);
            registrations.Add(registration);
        }
        return registrations;
    }

    private static ExpressionStatementSyntax CreateRegistrationExtensionSyntax(string className, string methodName)
    {
        var arguments = SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.IdentifierName("services"))));

        var expressionExpression = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(className),
                SyntaxFactory.IdentifierName(methodName)))
        .WithArgumentList(arguments);

        return SyntaxFactory.ExpressionStatement(expressionExpression);
    }

    private static ExpressionStatementSyntax CreateDecorationSyntax(string decoratedTypeName, string decoratorTypeName)
    {
        SyntaxNodeOrToken[] tokens =
            [
                SyntaxFactory.IdentifierName(decoratedTypeName),
                SyntaxFactory.Token(SyntaxKind.CommaToken),
                SyntaxFactory.IdentifierName(decoratorTypeName)
            ];

        var accessExpression = SyntaxFactory.MemberAccessExpression(
              SyntaxKind.SimpleMemberAccessExpression,
              SyntaxFactory.IdentifierName("services"),
              SyntaxFactory.GenericName(
                  SyntaxFactory.Identifier("Decorate"))
              .WithTypeArgumentList(
                  SyntaxFactory.TypeArgumentList(
                      SyntaxFactory.SeparatedList<TypeSyntax>(tokens))));

        var argumentList = SyntaxFactory.ArgumentList();

        var expression = SyntaxFactory.InvocationExpression(accessExpression)
              .WithArgumentList(argumentList);

        return SyntaxFactory.ExpressionStatement(expression);
    }

    private static ExpressionStatementSyntax CreateRegisterServicesCall()
    {
        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.IdentifierName("RegisterServices"))
            .WithArgumentList(
                SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList<ArgumentSyntax>(
                        SyntaxFactory.Argument(
                            SyntaxFactory.IdentifierName("serviceRegistry"))))));
    }

    private static ExpressionStatementSyntax CreateRegistrationSyntax(string? serviceType, string implementation, Lifetime lifetime, string? serviceName)
    {
        var keyed = serviceName is null ? string.Empty : "Keyed";
        var lifetimeName = lifetime switch
        {
            Lifetime.Singleton => $"Singleton",
            Lifetime.Scoped => "Scoped",
            Lifetime.Transient => "Transient",
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null)
        };
        var methodName = $"Add{keyed}{lifetimeName}";

        SyntaxNodeOrToken[] tokens;
        if (serviceType is null)
        {
            tokens = [SyntaxFactory.IdentifierName(implementation)];
        }
        else
        {
            tokens =
            [
                SyntaxFactory.IdentifierName(serviceType),
                SyntaxFactory.Token(SyntaxKind.CommaToken),
                SyntaxFactory.IdentifierName(implementation)
            ];
        }

        var accessExpression = SyntaxFactory.MemberAccessExpression(
              SyntaxKind.SimpleMemberAccessExpression,
              SyntaxFactory.IdentifierName("services"),
              SyntaxFactory.GenericName(
                  SyntaxFactory.Identifier(methodName))
              .WithTypeArgumentList(
                  SyntaxFactory.TypeArgumentList(
                      SyntaxFactory.SeparatedList<TypeSyntax>(tokens))));

        var argumentList = SyntaxFactory.ArgumentList();
        if (serviceName is not null)
        {
            argumentList = SyntaxFactory.ArgumentList(
                            SyntaxFactory.SingletonSeparatedList<ArgumentSyntax>(
                                SyntaxFactory.Argument(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.StringLiteralExpression,
                                        SyntaxFactory.Literal(serviceName)))));

        }

        var expression = SyntaxFactory.InvocationExpression(accessExpression)
              .WithArgumentList(argumentList);

        return SyntaxFactory.ExpressionStatement(expression);
    }
}
