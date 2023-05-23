﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using ZBase.Foundation.SourceGen;

namespace ZBase.Foundation.Mvvm.InternalUnionSourceGen
{
    [Generator]
    public class InternalUnionGenerator : IIncrementalGenerator
    {
        private const string IOBSERVABLE_OBJECT = "global::ZBase.Foundation.Mvvm.ComponentModel.IObservableObject";
        private const string IUNION_T = "global::ZBase.Foundation.Mvvm.Unions.IUnion<";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var projectPathProvider = SourceGenHelpers.GetSourceGenConfigProvider(context);

            var candidateProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, token) => IsSyntaxMatch(node, token),
                transform: static (syntaxContext, token) => GetSemanticMatch(syntaxContext, token)
            ).Where(static t => t is { });

            var candidateToIgnoreProvider = context.SyntaxProvider.CreateSyntaxProvider(
                predicate: static (node, token) => GeneratorHelpers.IsStructSyntaxMatch(node, token),
                transform: static (syntaxContext, token) => GetTypeInGenericUnionDeclaration(syntaxContext, token)
            ).Where(static t => t is { });

            var combined = candidateProvider.Collect()
                .Combine(candidateToIgnoreProvider.Collect())
                .Combine(context.CompilationProvider)
                .Combine(projectPathProvider);

            context.RegisterSourceOutput(combined, static (sourceProductionContext, source) => {
                GenerateOutput(
                    sourceProductionContext
                    , source.Left.Right
                    , source.Left.Left.Left
                    , source.Left.Left.Right
                    , source.Right.projectPath
                    , source.Right.outputSourceGenFiles
                );
            });
        }

        public static bool IsSyntaxMatch(SyntaxNode syntaxNode, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            
            if (syntaxNode is FieldDeclarationSyntax field)
            {
                if (field.HasAttributeCandidate("ZBase.Foundation.Mvvm.ComponentModel", "ObservableProperty"))
                {
                    return true;
                }
            }

            if (syntaxNode is MethodDeclarationSyntax method && method.ParameterList.Parameters.Count == 1)
            {
                if (method.HasAttributeCandidate("ZBase.Foundation.Mvvm.Input", "RelayCommand")
                    || method.HasAttributeCandidate("ZBase.Foundation.Mvvm.ViewBinding", "BindingProperty")
                    || method.HasAttributeCandidate("ZBase.Foundation.Mvvm.ViewBinding", "BindingCommand")
                )
                {
                    return true;
                }
            }

            if (syntaxNode is PropertyDeclarationSyntax property
                && property.Parent is ClassDeclarationSyntax classSyntax
                && classSyntax.BaseList.Types.Count > 0
            )
            {
                return true;
            }

            return false;
        }

        public static TypeRef GetSemanticMatch(
              GeneratorSyntaxContext context
            , CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();

            if (context.SemanticModel.Compilation.IsValidCompilation() == false)
            {
                return null;
            }

            var semanticModel = context.SemanticModel;

            if (context.Node is FieldDeclarationSyntax field)
            {
                return new TypeRef {
                    Syntax = field.Declaration.Type,
                    Symbol = semanticModel.GetTypeInfo(field.Declaration.Type).Type,
                };
            }

            if (context.Node is MethodDeclarationSyntax method)
            {
                var typeSyntax = method.ParameterList.Parameters[0].Type;

                return new TypeRef {
                    Syntax = typeSyntax,
                    Symbol = semanticModel.GetTypeInfo(typeSyntax).Type,
                };
            }

            if (context.Node is PropertyDeclarationSyntax property
                && property.Parent is ClassDeclarationSyntax classSyntax
                && classSyntax.DoesSemanticMatch(IOBSERVABLE_OBJECT, context.SemanticModel, token)
            )
            {
                return new TypeRef {
                    Syntax = property.Type,
                    Symbol = semanticModel.GetTypeInfo(property.Type).Type,
                };
            }

            return null;
        }

        public static ITypeSymbol GetTypeInGenericUnionDeclaration(
              GeneratorSyntaxContext context
            , CancellationToken token
        )
        {
            token.ThrowIfCancellationRequested();

            if (context.SemanticModel.Compilation.IsValidCompilation() == false
                || context.Node is not StructDeclarationSyntax structSyntax
                || structSyntax.BaseList == null
                || structSyntax.BaseList.Types.Count < 1
            )
            {
                return null;
            }

            var semanticModel = context.SemanticModel;

            foreach (var baseType in structSyntax.BaseList.Types)
            {
                var typeInfo = semanticModel.GetTypeInfo(baseType.Type, token);

                if (typeInfo.Type is INamedTypeSymbol interfaceSymbol)
                {
                    if (interfaceSymbol.IsGenericType
                       && interfaceSymbol.TypeParameters.Length == 1
                       && interfaceSymbol.ToFullName().StartsWith(IUNION_T)
                    )
                    {
                        return interfaceSymbol.TypeArguments[0];
                    }
                }

                if (TryGetMatchTypeArgument(typeInfo.Type.Interfaces, out var type)
                    || TryGetMatchTypeArgument(typeInfo.Type.AllInterfaces, out type)
                )
                {
                    return type;
                }
            }

            return null;

            static bool TryGetMatchTypeArgument(
                  ImmutableArray<INamedTypeSymbol> interfaces
                , out ITypeSymbol result
            )
            {
                foreach (var interfaceSymbol in interfaces)
                {
                    if (interfaceSymbol.IsGenericType
                        && interfaceSymbol.TypeParameters.Length == 1
                        && interfaceSymbol.ToFullName().StartsWith(IUNION_T)
                    )
                    {
                        result = interfaceSymbol.TypeArguments[0];
                        return true;
                    }
                }

                result = default;
                return false;
            }
        }

        private static void GenerateOutput(
              SourceProductionContext context
            , Compilation compilation
            , ImmutableArray<TypeRef> candidates
            , ImmutableArray<ITypeSymbol> candidatesToIgnore
            , string projectPath
            , bool outputSourceGenFiles
        )
        {
            if (candidates.Length < 1)
            {
                return;
            }

            context.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                SourceGenHelpers.ProjectPath = projectPath;

                var declaration = new InternalUnionDeclaration(candidates, candidatesToIgnore);

                declaration.GenerateUnionForValueTypes(
                      context
                    , compilation
                    , outputSourceGenFiles
                    , s_errorDescriptor
                );

                declaration.GenerateUnionForRefTypes(
                      context
                    , compilation
                    , outputSourceGenFiles
                    , s_errorDescriptor
                );

                declaration.GenerateStaticClass(
                      context
                    , compilation
                    , outputSourceGenFiles
                    , s_errorDescriptor
                );
            }
            catch (Exception e)
            {
                context.ReportDiagnostic(Diagnostic.Create(
                      s_errorDescriptor
                    , null
                    , e.ToUnityPrintableString()
                ));
            }
        }

        private static readonly DiagnosticDescriptor s_errorDescriptor
            = new("SG_INTERNAL_UNIONS_01"
                , "Internal Union Generator Error"
                , "This error indicates a bug in the Internal Union source generators. Error message: '{0}'."
                , "ZBase.Foundation.Mvvm.IObservableObject"
                , DiagnosticSeverity.Error
                , isEnabledByDefault: true
                , description: ""
            );
    }
}
