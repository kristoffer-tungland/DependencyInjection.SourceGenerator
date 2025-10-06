using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using DependencyInjection.SourceGenerator.LightInject.Contracts.Attributes;
using DependencyInjection.SourceGenerator.Contracts.Enums;
using DependencyInjection.SourceGenerator.Shared;

namespace DependencyInjection.SourceGenerator.LightInject;

[Generator]
public class DependencyInjectionRegistrationGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
        context.RegisterForSyntaxNotifications(() => new ClassAttributeReceiver(additionalClassAttributes: [nameof(RegisterCompositionRootAttribute)]));
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // Get first existing CompositionRoot class
        var compositionRoot = context.Compilation.SyntaxTrees
            .SelectMany(x => x.GetRoot().DescendantNodes())
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(x => x.Identifier.Text == "CompositionRoot");

        if (compositionRoot is not null && !IsPartial(compositionRoot))
        {
            var descriptor = new DiagnosticDescriptor("DIL01", "CompositionRoot not patial", "CompositionRoot must be partial", "LightInject", DiagnosticSeverity.Error, true);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, compositionRoot.GetLocation()));
            return;
        }

        // If CompositionRoot class exists, get namespace, if not, use root namespace of project
        var @namespace = GetDefaultNamespace(context, compositionRoot);

        var classesToRegister = RegistrationCollector.GetTypes(context);
        var registerAllTypes = RegistrationCollector.GetRegisterAllTypes(context);
        var factoryDefinitions = new List<FactoryDefinition>();

        var source = GenerateCompositionRoot(context, compositionRoot is not null, @namespace, classesToRegister, registerAllTypes, factoryDefinitions);
        var sourceText = source.ToFullString();
        context.AddSource("CompositionRoot.g.cs", SourceText.From(sourceText, Encoding.UTF8));

        foreach (var factory in factoryDefinitions)
        {
            var factorySource = GenerateFactory(factory);
            context.AddSource($"{factory.ImplementationName}.Factory.g.cs", SourceText.From(factorySource.ToFullString(), Encoding.UTF8));
        }
    }

    internal static string GetDefaultNamespace(GeneratorExecutionContext context, ClassDeclarationSyntax? compositionRoot)
    {
        if (compositionRoot?.Parent is NamespaceDeclarationSyntax namespaceDeclarationSyntax)
            return namespaceDeclarationSyntax.Name.ToString();

        if (compositionRoot?.Parent is FileScopedNamespaceDeclarationSyntax fileScopedNamespaceDeclarationSyntax)
            return fileScopedNamespaceDeclarationSyntax.Name.ToString();

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

    private static CompilationUnitSyntax GenerateCompositionRoot(GeneratorExecutionContext context, bool userDefinedCompositionRoot, string @namespace, IEnumerable<INamedTypeSymbol> classesToRegister, List<Registration> additionalRegistrations, List<FactoryDefinition> factoryDefinitions)
    {
        var modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));

        if (userDefinedCompositionRoot)
            modifiers = modifiers.Add(SyntaxFactory.Token(SyntaxKind.PartialKeyword));

        var classModifiers = SyntaxFactory.TokenList(modifiers);

        var bodyMembers = new List<ExpressionStatementSyntax>();
        if (userDefinedCompositionRoot)
            bodyMembers.Add(CreateRegisterServicesCall());

        foreach (var type in classesToRegister)
        {
            var registrations = RegistrationMapper.CreateRegistration(type);
            foreach (var registration in registrations)
            {
                bodyMembers.Add(CreateServiceRegistration(registration.ServiceType, registration.ImplementationTypeName, registration.Lifetime, registration.ServiceName));
            }

            var decoration = DecorationMapper.CreateDecoration(type);
            if (decoration is not null)
                bodyMembers.Add(CreateServiceDecoration(decoration.DecoratedTypeName, decoration.DecoratorTypeName));

            var registrationSyntax = CreateRegistrationExtensions(context, type);
            if (registrationSyntax is not null)
                bodyMembers.Add(registrationSyntax);

            var factory = FactoryMapper.CreateFactory(type, registrations, @namespace);
            if (factory is not null)
            {
                factoryDefinitions.Add(factory);
                bodyMembers.Add(CreateServiceRegistration(factory.InterfaceFullName, factory.ImplementationFullName, Lifetime.Singleton, null));
            }

        }

        foreach (var registration in additionalRegistrations)
        {
            bodyMembers.Add(CreateServiceRegistration(registration.ServiceType, registration.ImplementationTypeName, registration.Lifetime, registration.ServiceName));
        }

        var body = SyntaxFactory.Block(bodyMembers.ToArray());


        var serviceRegistryType = CreateServiceRegistrySyntax("IServiceRegistry");

        var methodParameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier("serviceRegistry"))
                                            .WithType(serviceRegistryType);

        var methodDeclaration = SyntaxFactory.MethodDeclaration(SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.VoidKeyword)), SyntaxFactory.Identifier("Compose"))
                                .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword))
                                .AddParameterListParameters(methodParameter)
                                .WithBody(body);

        var compositionRootSyntax = CreateServiceRegistrySyntax("ICompositionRoot");
        var baseType = SyntaxFactory.SimpleBaseType(compositionRootSyntax);

        var classDeclaration = SyntaxFactory.ClassDeclaration("CompositionRoot")
                        .WithModifiers(classModifiers)
                        .AddBaseListTypes(baseType)
                        .AddMembers(methodDeclaration);

        return Trivia.CreateCompilationUnitSyntax(classDeclaration, @namespace);
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

        var serviceFactoryType = SyntaxFactory.ParseTypeName("global::LightInject.IServiceFactory");
        var fieldDeclaration = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(serviceFactoryType)
                    .WithVariables(
                        SyntaxFactory.SingletonSeparatedList(
                            SyntaxFactory.VariableDeclarator(CreateIdentifier("_serviceFactory")))))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PrivateKeyword), SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword)));

        var constructorDeclaration = SyntaxFactory.ConstructorDeclaration(SyntaxFactory.Identifier(factory.ImplementationName))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(
                SyntaxFactory.ParameterList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Parameter(CreateIdentifier("serviceFactory"))
                            .WithType(serviceFactoryType))))
            .WithBody(
                SyntaxFactory.Block(
                    SyntaxFactory.ExpressionStatement(
                        SyntaxFactory.AssignmentExpression(
                            SyntaxKind.SimpleAssignmentExpression,
                            SyntaxFactory.IdentifierName(CreateIdentifier("_serviceFactory")),
                            SyntaxFactory.IdentifierName(CreateIdentifier("serviceFactory"))))));

        var methodDeclaration = SyntaxFactory.MethodDeclaration(SyntaxFactory.ParseTypeName(factory.ReturnType), SyntaxFactory.Identifier("Create"))
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
            .WithParameterList(SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(factory.Parameters.Select(CreateParameter))))
            .WithBody(
                SyntaxFactory.Block(
                    SyntaxFactory.ReturnStatement(
                        SyntaxFactory.CastExpression(
                            SyntaxFactory.ParseTypeName(factory.ReturnType),
                            CreateServiceFactoryInvocation(factory)))));

        var classDeclaration = SyntaxFactory.ClassDeclaration(factory.ImplementationName)
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.SealedKeyword)))
            .AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(factory.InterfaceFullName)))
            .AddMembers(fieldDeclaration, constructorDeclaration, methodDeclaration);

        return Trivia.CreateCompilationUnitSyntax(new MemberDeclarationSyntax[] { interfaceDeclaration, classDeclaration }, factory.Namespace);
    }

    private static ExpressionSyntax CreateServiceFactoryInvocation(FactoryDefinition factory)
    {
        var target = SyntaxFactory.MemberAccessExpression(
            SyntaxKind.SimpleMemberAccessExpression,
            SyntaxFactory.IdentifierName(CreateIdentifier("_serviceFactory")),
            SyntaxFactory.IdentifierName("GetInstance"));

        var arguments = new List<ArgumentSyntax>
        {
            SyntaxFactory.Argument(
                SyntaxFactory.TypeOfExpression(SyntaxFactory.ParseTypeName(factory.ServiceType ?? factory.ImplementationTypeName)))
        };

        if (!string.IsNullOrEmpty(factory.ServiceName))
        {
            arguments.Add(
                SyntaxFactory.Argument(
                    SyntaxFactory.LiteralExpression(
                        SyntaxKind.StringLiteralExpression,
                        SyntaxFactory.Literal(factory.ServiceName!))));
        }

        if (factory.Parameters.Count > 0)
        {
            var arrayType = SyntaxFactory.ArrayType(SyntaxFactory.ParseTypeName("global::System.Object"))
                .WithRankSpecifiers(
                    SyntaxFactory.SingletonList(
                        SyntaxFactory.ArrayRankSpecifier(
                            SyntaxFactory.SingletonSeparatedList<ExpressionSyntax>(
                                SyntaxFactory.OmittedArraySizeExpression()))));

            var initializer = SyntaxFactory.InitializerExpression(
                SyntaxKind.ArrayInitializerExpression,
                SyntaxFactory.SeparatedList<ExpressionSyntax>(factory.Parameters.Select(p => SyntaxFactory.IdentifierName(CreateIdentifier(p.Name)))));

            var arrayCreation = SyntaxFactory.ArrayCreationExpression(arrayType).WithInitializer(initializer);
            arguments.Add(SyntaxFactory.Argument(arrayCreation));
        }

        return SyntaxFactory.InvocationExpression(target)
            .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
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

    internal static ExpressionStatementSyntax? CreateRegistrationExtensions(GeneratorExecutionContext context, INamedTypeSymbol type)
    {
        var attribute = TypeHelper.GetAttributes<RegisterCompositionRootAttribute>(type.GetAttributes()).FirstOrDefault();
        if (attribute is null)
            return null;

        if (!TypeImplementsCompositionRoot(type))
        {
            var diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor(
                "DIL0002",
                "Invalid composition root implementation",
                "Class {0} does not implement ICompositionRoot",
                "InvalidConfig",
                DiagnosticSeverity.Error,
                true), null, type.Name);
            context.ReportDiagnostic(diagnostic);
            return null;
        }

        return CreateRegisterFromSyntax(type);
    }

    private static ExpressionStatementSyntax? CreateRegisterFromSyntax(INamedTypeSymbol type)
    {
        var typeName = TypeHelper.GetFullName(type);
        var invocationExpression = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("serviceRegistry"),
                SyntaxFactory.GenericName(
                    SyntaxFactory.Identifier("RegisterFrom"))
                .WithTypeArgumentList(
                    SyntaxFactory.TypeArgumentList(
                        SyntaxFactory.SingletonSeparatedList<TypeSyntax>(
                            SyntaxFactory.IdentifierName(typeName))))));

        return SyntaxFactory.ExpressionStatement(invocationExpression);
    }

    private static bool TypeImplementsCompositionRoot(INamedTypeSymbol type)
    {
        return type.AllInterfaces.Any(x => TypeHelper.GetFullName(x) == "global::LightInject.ICompositionRoot");
    }

    private static ExpressionStatementSyntax CreateServiceDecoration(string decoratedTypeName, string decoratorTypeName)
    {
        SyntaxNodeOrToken[] tokens =
           [
               SyntaxFactory.IdentifierName(decoratedTypeName),
                SyntaxFactory.Token(SyntaxKind.CommaToken),
                SyntaxFactory.IdentifierName(decoratorTypeName)
           ];

        var accessExpression = SyntaxFactory.MemberAccessExpression(
              SyntaxKind.SimpleMemberAccessExpression,
              SyntaxFactory.IdentifierName("serviceRegistry"),
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

    private static ExpressionStatementSyntax CreateServiceRegistration(string? serviceType, string implementation, Lifetime lifetime, string? serviceName)
    {
        var lifetimeName = lifetime switch
        {
            Lifetime.Singleton => "PerContainerLifetime",
            Lifetime.Scoped => "PerScopeLifetime",
            Lifetime.Transient => "PerRequestLifeTime",
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, null)
        };


        var args = new List<SyntaxNodeOrToken>();
        if (!string.IsNullOrEmpty(serviceName))
        {
            var serviceNameSyntax = SyntaxFactory.Argument(
                                                       SyntaxFactory.LiteralExpression(
                                                           SyntaxKind.StringLiteralExpression,
                                                           SyntaxFactory.Literal(serviceName!)));

            args.Add(serviceNameSyntax);
            args.Add(SyntaxFactory.Token(SyntaxKind.CommaToken));
        }

        var lifetimeIdentifierSyntax = CreateServiceRegistrySyntax(lifetimeName);
        var lifetimeSyntaxArgument = SyntaxFactory.Argument(
            SyntaxFactory.ObjectCreationExpression(lifetimeIdentifierSyntax)
                .WithArgumentList(SyntaxFactory.ArgumentList()));

        args.Add(lifetimeSyntaxArgument);

        var argumentList = SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList<ArgumentSyntax>(args));

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

        return SyntaxFactory.ExpressionStatement(
            SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("serviceRegistry"),
                    SyntaxFactory.GenericName(
                        SyntaxFactory.Identifier("Register"))
                    .WithTypeArgumentList(
                        SyntaxFactory.TypeArgumentList(
                            SyntaxFactory.SeparatedList<TypeSyntax>(
                                tokens)))))
             .WithArgumentList(argumentList));
    }

    private static bool IsPartial(ClassDeclarationSyntax compositionRoot)
    {
        return compositionRoot.Modifiers.Any(x => x.Text == "partial");
    }

    internal static QualifiedNameSyntax CreateServiceRegistrySyntax(string className)
    {
        return SyntaxFactory.QualifiedName(
            SyntaxFactory.AliasQualifiedName(
                SyntaxFactory.IdentifierName(
                    SyntaxFactory.Token(SyntaxKind.GlobalKeyword)),
                SyntaxFactory.IdentifierName("LightInject")),
            SyntaxFactory.IdentifierName(className));

        //var attribute = SyntaxFactory.Attribute(name);
        //var attributeList = SyntaxFactory.AttributeList(SyntaxFactory.SingletonSeparatedList(attribute));

        //return SyntaxFactory.SingletonList(attributeList);
    }
}
