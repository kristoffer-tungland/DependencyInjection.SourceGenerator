﻿using Microsoft.CodeAnalysis;
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
                predicate: static (s, _) => IsClassOrMethodWithAttributes(s),
                transform: static (ctx, _) => GetSymbolToRegister(ctx))
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

    private static bool IsClassOrMethodWithAttributes(SyntaxNode node)
    {
        return node is TypeDeclarationSyntax typeDeclaration && typeDeclaration.AttributeLists.Count > 0
            || node is MethodDeclarationSyntax methodDeclaration && methodDeclaration.AttributeLists.Count > 0;
    }

    private static ISymbol? GetSymbolToRegister(GeneratorSyntaxContext context)
    {
        if (context.Node is TypeDeclarationSyntax typeDeclaration)
        {
            var semanticModel = context.SemanticModel;
            if (semanticModel.GetDeclaredSymbol(typeDeclaration) is not INamedTypeSymbol classSymbol)
                return null;

            foreach (var attribute in classSymbol.GetAttributes())
            {
                if (attribute.AttributeClass?.Name is (nameof(RegisterAttribute)) or (nameof(DecorateAttribute)))
                {
                    return classSymbol;
                }
            }
        }
        else if (context.Node is MethodDeclarationSyntax methodDeclaration)
        {
            var semanticModel = context.SemanticModel;
            var methodSymbol = semanticModel.GetDeclaredSymbol(methodDeclaration) as IMethodSymbol;
            if (methodSymbol != null &&
                methodSymbol.ReturnsVoid == false &&
                methodSymbol.Parameters.Length == 1 &&
                methodSymbol.Parameters[0].Type.ToDisplayString() == "System.IServiceProvider")
            {
                foreach (var attribute in methodSymbol.GetAttributes())
                {
                    if (attribute.AttributeClass?.Name == nameof(RegisterAttribute))
                    {
                        return methodSymbol;
                    }
                }
            }
        }

        return null;
    }

    private static void Execute(Compilation compilation, ImmutableArray<ISymbol?> symbolsToRegister, SourceProductionContext context)
    {
        var @namespace = "Microsoft.Extensions.DependencyInjection";
        var safeAssemblyName = EscapeAssemblyNameToMethodName(compilation.AssemblyName);
        var extensionName = "Add" + safeAssemblyName;

        var bodyMembers = new List<ExpressionStatementSyntax>();

        var includeScrutor = false;

        foreach (var symbol in symbolsToRegister)
        {
            if (symbol is INamedTypeSymbol classSymbol)
            {
                var hasDecorators = ProcessClassSymbol(classSymbol, bodyMembers);
                if (hasDecorators)
                {
                    includeScrutor = true;
                }
            }
            else if (symbol is IMethodSymbol methodSymbol)
            {
                ProcessMethodSymbol(methodSymbol, bodyMembers, context);
            }
        }

        RegisterAllHandler.Process(compilation, bodyMembers);

        var source = GenerateExtensionMethod(extensionName, @namespace, bodyMembers, includeScrutor);
        var sourceText = source.ToFullString();
        context.AddSource("ServiceRegistrations.g.cs", SourceText.From(sourceText, Encoding.UTF8));
    }

    private static bool ProcessClassSymbol(INamedTypeSymbol classSymbol, List<ExpressionStatementSyntax> bodyMembers)
    {
        var registrations = RegistrationMapper.CreateRegistration(classSymbol);
        foreach (var registration in registrations)
        {
            var (registrationExpression, factoryExpression) = RegistrationMapper.CreateRegistrationSyntaxFromClass(
                registration.ServiceType, 
                registration.ImplementationTypeName, 
                registration.Lifetime, 
                registration.ServiceName, 
                registration.IncludeFactory);
            bodyMembers.Add(registrationExpression);
            if (factoryExpression is not null)
            {
                bodyMembers.Add(factoryExpression);
            }
        }

        var decorations = DecorationMapper.CreateDecoration(classSymbol);
        var hasDecorators = false;
        foreach (var decoration in decorations)
        {
            bodyMembers.Add(CreateDecorationSyntax(decoration.DecoratedTypeName, decoration.DecoratorTypeName));
            hasDecorators = true;
        }

        return hasDecorators;
    }

    private static void ProcessMethodSymbol(IMethodSymbol methodSymbol, List<ExpressionStatementSyntax> bodyMembers, SourceProductionContext context)
    {
        // Ensure the method is static
        if (!methodSymbol.IsStatic)
        {
            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor(
                "DI001",
                "Method Registration",
                "Register method '{0}' must be static",
                "DependencyInjection",
                DiagnosticSeverity.Error,
                isEnabledByDefault: true),
                Location.None,
                methodSymbol.Name));
            return;
        }

        // Handle method registration
        var registrations = RegistrationMapper.CreateRegistrationFromMethod(methodSymbol);
        foreach (var registration in registrations)
        {
            // Create the registration expression using type symbols
            var registrationExpression = RegistrationMapper.CreateRegistrationSyntaxFromMethod(registration);
            bodyMembers.Add(registrationExpression);
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

    private static CompilationUnitSyntax GenerateExtensionMethod(string extensionName, string @namespace, List<ExpressionStatementSyntax> bodyMembers, bool includeScrutor)
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
