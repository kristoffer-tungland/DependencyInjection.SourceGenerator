using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Text;
using DependencyInjection.SourceGenerator.Microsoft.Helpers;
using System.Collections.Immutable;
using DependencyInjection.SourceGenerator.Microsoft.Enums;
using Microsoft.Extensions.DependencyInjection;
using CodeGenHelpers.Internals;

namespace DependencyInjection.SourceGenerator.Microsoft;

public record RegistrationExtension(string ClassFullName, string MethodName, List<Diagnostic> Errors);

[Generator]
public class DependencyInjectionRegistrationGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var declarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => IsClassWithAttributes(s),
                transform: static (ctx, _) => GetClassToRegister(ctx))
            .Where(static m => m is not null);

        var compilationAndDeclarations = context.CompilationProvider.Combine(declarations.Collect());
        context.RegisterSourceOutput(compilationAndDeclarations, static (spc, source) => Execute(source.Left, source.Right, spc));
        context.RegisterPostInitializationOutput(static spc =>
        {
            spc.AddSource("RegisterAttribute.g.cs", SourceText.From(AttributeSourceTexts.RegisterAttributeText, Encoding.UTF8));
            spc.AddSource("RegisterAllAttribute.g.cs", SourceText.From(AttributeSourceTexts.RegisterAllAttributeText, Encoding.UTF8));
            spc.AddSource("DecorateAttribute.g.cs", SourceText.From(AttributeSourceTexts.DecorateAttributeText, Encoding.UTF8));
        });
    }

    private static bool IsClassWithAttributes(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax classDeclaration && classDeclaration.AttributeLists.Count > 0;
    }

    private static INamedTypeSymbol? GetClassToRegister(GeneratorSyntaxContext context)
    {
        if (context.Node is not ClassDeclarationSyntax classDeclaration)
            return null;

        var semanticModel = context.SemanticModel;
        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
            return null;

        foreach (var attribute in classSymbol.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == nameof(RegisterAttribute)
                || attribute.AttributeClass?.Name == nameof(DecorateAttribute))
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
        var extensionName = "Add" + safeAssemblyName;

        var bodyMembers = new List<ExpressionStatementSyntax>();

        var includeScrutor = false;

        foreach (var classSymbol in classesToRegister)
        {
            if (classSymbol is null)
                continue;

            var registrations = RegistrationMapper.CreateRegistration(classSymbol);
            foreach (var registration in registrations)
            {
                bodyMembers.Add(RegistrationMapper.CreateRegistrationSyntax(registration.ServiceType, registration.ImplementationTypeName, registration.Lifetime, registration.ServiceName));
            }

            var decoration = DecorationMapper.CreateDecoration(classSymbol);
            if (decoration is not null)
            {
                bodyMembers.Add(CreateDecorationSyntax(decoration.DecoratedTypeName, decoration.DecoratorTypeName));
                includeScrutor = true;
            }
        }

        RegisterAllHandler.Process(compilation, bodyMembers);

        var source = GenerateExtensionMethod(context, extensionName, @namespace, bodyMembers, includeScrutor);
        var sourceText = source.ToFullString();
        context.AddSource("ServiceRegistrations.g.cs", SourceText.From(sourceText, Encoding.UTF8));
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

    private static CompilationUnitSyntax GenerateExtensionMethod(SourceProductionContext context, string extensionName, string @namespace, List<ExpressionStatementSyntax> bodyMembers, bool includeScrutor)
    {
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

        var classModifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword), SyntaxFactory.Token(SyntaxKind.StaticKeyword));
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

        var usingDirectives = new List<UsingDirectiveSyntax> { dependencyInjectionUsingDirective };
        if (includeScrutor)
        {
            usingDirectives.Add(SyntaxFactory.UsingDirective(SyntaxFactory.IdentifierName("Scrutor")));
        }

        return Trivia.CreateCompilationUnitSyntax(classDeclaration, @namespace, [.. usingDirectives]);
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

    
}
