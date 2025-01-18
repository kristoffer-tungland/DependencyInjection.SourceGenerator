using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using DependencyInjection.SourceGenerator.Microsoft.Helpers;
using DependencyInjection.SourceGenerator.Microsoft.Attributes;
using System.Collections.Immutable;
using DependencyInjection.SourceGenerator.Microsoft.Enums;

namespace DependencyInjection.SourceGenerator.Microsoft;

public record RegistrationExtension(string ClassFullName, string MethodName, List<Diagnostic> Errors);

[Generator]
public class DependencyInjectionRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsClassWithAttribute(s),
                transform: static (ctx, _) => GetClassToRegister(ctx))
            .Where(static m => m is not null);

        var compilationAndClasses = context.CompilationProvider.Combine(classDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (spc, source) => Execute(source.Left, source.Right, spc));
        context.RegisterPostInitializationOutput(static spc =>
        {
            spc.AddSource("RegisterAttribute.g.cs", SourceText.From(AttributeSourceTexts.RegisterAttributeText, Encoding.UTF8));
            spc.AddSource("RegisterAllAttribute.g.cs", SourceText.From(AttributeSourceTexts.RegisterAllAttributeText, Encoding.UTF8));
            spc.AddSource("DecorateAttribute.g.cs", SourceText.From(AttributeSourceTexts.DecorateAttributeText, Encoding.UTF8));
        });
    }

    private static bool IsClassWithAttribute(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration &&
               classDeclaration.AttributeLists.Count > 0;
    }

    private static INamedTypeSymbol? GetClassToRegister(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
            return null;

        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == nameof(RegistrationExtensionAttribute))
            {
                return classSymbol;
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<INamedTypeSymbol?> classesToRegister, SourceProductionContext context)
    {
        var @namespace = "Microsoft.Extensions.DependencyInjection";
        var safeAssemblyName = EscapeAssemblyNameToMethodName(compilation.AssemblyName);

        foreach (var classSymbol in classesToRegister)
        {
            if (classSymbol is null)
                continue;

            var extensionName = "Add" + safeAssemblyName + classSymbol.Name;
            var source = GenerateExtensionMethod(context, extensionName, @namespace, classSymbol);
            var sourceText = source.ToFullString();
            context.AddSource($"{classSymbol.Name}ServiceCollectionExtensions.g.cs", SourceText.From(sourceText, Encoding.UTF8));
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

    private static CompilationUnitSyntax GenerateExtensionMethod(SourceProductionContext context, string extensionName, string @namespace, INamedTypeSymbol classToRegister)
    {
        var bodyMembers = new List<ExpressionStatementSyntax>();

        var registrations = RegistrationMapper.CreateRegistration(classToRegister);
        foreach (var registration in registrations)
        {
            bodyMembers.Add(CreateRegistrationSyntax(registration.ServiceType, registration.ImplementationTypeName, registration.Lifetime, registration.ServiceName));
        }

        var decoration = DecorationMapper.CreateDecoration(classToRegister);
        if (decoration is not null)
            bodyMembers.Add(CreateDecorationSyntax(decoration.DecoratedTypeName, decoration.DecoratorTypeName));

        var registrationExtensions = CreateRegistrationExtensions(classToRegister);

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

        var methodModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword));

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

        var classModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword), SyntaxFactory.Token(SyntaxKind.PartialKeyword));
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

        return Trivia.CreateCompilationUnitSyntax(classDeclaration, @namespace, new[] { dependencyInjectionUsingDirective });
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

    private static ExpressionStatementSyntax CreateRegistrationSyntax(string? serviceType, string implementation, ServiceLifetime lifetime, string? serviceName)
    {
        var keyed = serviceName is null ? string.Empty : "Keyed";
        var methodName = $"Add{keyed}{lifetime}";

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
