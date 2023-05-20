﻿using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using ZBase.Foundation.SourceGen;

namespace ZBase.Foundation.Mvvm.BinderSourceGen
{
    public partial class BinderDeclaration
    {
        public const string IBINDER_INTERFACE = "global::ZBase.Foundation.Mvvm.ViewBinding.IBinder";
        public const string BINDING_PROPERTY_ATTRIBUTE = "global::ZBase.Foundation.Mvvm.ViewBinding.BindingPropertyAttribute";
        public const string BINDING_COMMAND_ATTRIBUTE = "global::ZBase.Foundation.Mvvm.ViewBinding.BindingCommandAttribute";
        public const string BINDING_PROPERTY = "global::ZBase.Foundation.Mvvm.ViewBinding.BindingProperty";
        public const string BINDING_COMMAND = "global::ZBase.Foundation.Mvvm.ViewBinding.BindingCommand";
        public const string CONVERTER = "global::ZBase.Foundation.Mvvm.ViewBinding.Converter";
        public const string UNION_TYPE = "global::ZBase.Foundation.Mvvm.Unions.Union";
        public const string MONO_BEHAVIOUR_TYPE = "global::UnityEngine.MonoBehaviour";

        public ClassDeclarationSyntax Syntax { get; }

        public INamedTypeSymbol Symbol { get; }

        public bool IsBaseBinder { get; }

        public bool ReferenceUnityEngine { get; }

        public ImmutableArray<BindingPropertyRef> BindingPropertyRefs { get; }

        public ImmutableArray<BindingCommandRef> BindingCommandRefs { get; }

        public ImmutableArray<ITypeSymbol> NonUnionTypes { get; }

        public BinderDeclaration(ClassDeclarationSyntax candidate, SemanticModel semanticModel, CancellationToken token)
        {
            using var bindingPropertyRefs = ImmutableArrayBuilder<BindingPropertyRef>.Rent();
            using var bindingCommandRefs = ImmutableArrayBuilder<BindingCommandRef>.Rent();
            using var diagnosticBuilder = ImmutableArrayBuilder<DiagnosticInfo>.Rent();

            var bindingPropertyNames = new HashSet<string>();
            var converterNames = new HashSet<string>();
            var bindingCommandNames = new HashSet<string>();

            Syntax = candidate;
            Symbol = semanticModel.GetDeclaredSymbol(candidate, token);

            if (Symbol.BaseType != null && Symbol.BaseType.TypeKind == TypeKind.Class)
            {
                if (Symbol.BaseType.ImplementsInterface(IBINDER_INTERFACE))
                {
                    IsBaseBinder = true;
                }
            }

            foreach (var assembly in Symbol.ContainingModule.ReferencedAssemblySymbols)
            {
                if (assembly.ToDisplayString().StartsWith("UnityEngine,"))
                {
                    ReferenceUnityEngine = true;
                    break;
                }
            }

            var members = Symbol.GetMembers();
            var nonUnionTypeFilter = new Dictionary<string, ITypeSymbol>(members.Length);

            foreach (var member in members)
            {
                if (member is IMethodSymbol method)
                {
                    var bindingPropAttribute = method.GetAttribute(BINDING_PROPERTY_ATTRIBUTE);
                    var bindingCommandAttribute = method.GetAttribute(BINDING_COMMAND_ATTRIBUTE);

                    if (bindingPropAttribute != null && method.Parameters.Length == 1)
                    {
                        var parameter = method.Parameters[0];

                        if (parameter.RefKind is not (RefKind.None or RefKind.In or RefKind.Ref))
                        {
                            continue;
                        }

                        var argumentType = parameter.Type;
                        var isNotUnion = argumentType.ToFullName() != UNION_TYPE;

                        bindingPropertyRefs.Add(new BindingPropertyRef {
                            Symbol = method,
                            IsParameterTypeNotUnion = isNotUnion,
                            Parameter = parameter,
                        });

                        if (isNotUnion == false)
                        {
                            continue;
                        }

                        var typeName = argumentType.ToFullName();

                        if (nonUnionTypeFilter.ContainsKey(typeName) == false)
                        {
                            nonUnionTypeFilter[typeName] = argumentType;
                        }
                    }
                    else if (bindingCommandAttribute != null
                        && method.IsPartialDefinition
                        && method.ReturnsVoid
                        && method.Parameters.Length < 2
                    )
                    {
                        IParameterSymbol param = null;

                        if (method.Parameters.Length == 1
                            && method.Parameters[0].RefKind is (RefKind.None or RefKind.In or RefKind.Ref)
                        )
                        {
                            param = method.Parameters[0];
                        }

                        bindingCommandRefs.Add(new BindingCommandRef {
                            Symbol = method,
                            Parameter = param,
                        });
                    }

                    continue;
                }

                if (member is IFieldSymbol field)
                {
                    var typeName = field.Type.ToFullName();
                    
                    if (typeName == BINDING_PROPERTY)
                    {
                        bindingPropertyNames.Add(field.Name);
                    }
                    else if (typeName == CONVERTER)
                    {
                        converterNames.Add(field.Name);
                    }
                    else if (typeName == BINDING_COMMAND)
                    {
                        bindingCommandNames.Add(field.Name);
                    }
                }
            }

            using var nonUnionTypes = ImmutableArrayBuilder<ITypeSymbol>.Rent();
            nonUnionTypes.AddRange(nonUnionTypeFilter.Values);

            BindingPropertyRefs = bindingPropertyRefs.ToImmutable();
            BindingCommandRefs = bindingCommandRefs.ToImmutable();
            NonUnionTypes = nonUnionTypes.ToImmutable();

            foreach (var bindingPropertyRef in BindingPropertyRefs)
            {
                var bindingPropName = BindingPropertyName(bindingPropertyRef);
                var converterName = ConverterName(bindingPropertyRef);
                bindingPropertyRef.SkipBindingProperty = bindingPropertyNames.Contains(bindingPropName);
                bindingPropertyRef.SkipConverter = converterNames.Contains(converterName);

                bindingPropertyRef.Symbol.GatherForwardedAttributes(
                      semanticModel
                    , token
                    , diagnosticBuilder
                    , out var fieldAttributes
                    , out _
                    , DiagnosticDescriptors.InvalidFieldTargetedAttributeOnBindingPropertyMethod
                );

                bindingPropertyRef.ForwardedFieldAttributes = fieldAttributes;
            }

            foreach (var bindingCommandRef in BindingCommandRefs)
            {
                var bindingPropName = BindingCommandName(bindingCommandRef);
                bindingCommandRef.SkipBindingCommand = bindingCommandNames.Contains(bindingPropName);

                bindingCommandRef.Symbol.GatherForwardedAttributes(
                      semanticModel
                    , token
                    , diagnosticBuilder
                    , out var fieldAttributes
                    , out _
                    , DiagnosticDescriptors.InvalidFieldTargetedAttributeOnBindingCommandMethod
                );

                bindingCommandRef.ForwardedFieldAttributes = fieldAttributes;
            }
        }

        public class BindingPropertyRef
        {
            public IMethodSymbol Symbol { get; set; }

            public IParameterSymbol Parameter { get; set; }

            public bool IsParameterTypeNotUnion { get; set; }

            public bool SkipBindingProperty { get; set; }

            public bool SkipConverter { get; set; }

            public ImmutableArray<AttributeInfo> ForwardedFieldAttributes { get; set; }
        }

        public class BindingCommandRef
        {
            public IMethodSymbol Symbol { get; set; }

            public IParameterSymbol Parameter { get; set; }

            public bool SkipBindingCommand { get; set; }

            public ImmutableArray<AttributeInfo> ForwardedFieldAttributes { get; set; }
        }
    }
}