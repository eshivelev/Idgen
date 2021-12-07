using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Idgen
{
    [AttributeUsage(AttributeTargets.Struct)]
    public class IdentifierAttribute : Attribute { }

    [Generator]
    public class StronglyTypedIdGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }
        public void Execute(GeneratorExecutionContext context)
        {
            if (context.SyntaxReceiver is not SyntaxReceiver receiver)
                return;
            foreach (var declaration in receiver.CandidateDeclarations)
            {
                var model = context.Compilation.GetSemanticModel(declaration.SyntaxTree);
                var type = ModelExtensions.GetDeclaredSymbol(model, declaration);
                if (type is null)
                    continue;
                if (!IsStronglyTypedId(type))
                    continue;
                string fullNamespace = type.ContainingNamespace.ToDisplayString();
                string typeName = type.Name;
                var sourceText = $@"#nullable enable
using System;
using System.Diagnostics;
namespace {fullNamespace}
{{
    [DebuggerDisplay(""{{ToString(),nq}}"")]
    public readonly partial struct {typeName} : IEquatable<{typeName}>, IComparable<{typeName}>
    {{
        private readonly long _value;
        private {typeName}(long value) => _value = value;
        public int CompareTo({typeName} other) => _value.CompareTo(other._value);
        public bool Equals({typeName} other) => _value == other._value;
        public override bool Equals(object? obj) => obj is {typeName} id && Equals(id);
        public override int GetHashCode() => _value.GetHashCode();
        public override string ToString() => _value.ToString();
        public static explicit operator {typeName}(long value) => new(value);
        public static explicit operator long({typeName} value) => value._value;
        public static bool operator ==({typeName} left, {typeName} right) => left.Equals(right);
        public static bool operator !=({typeName} left, {typeName} right) => !left.Equals(right);
        public static bool operator <({typeName} left, {typeName} right) => left._value < right._value;
        public static bool operator >({typeName} left, {typeName} right) => left._value > right._value;
        public static bool operator <=({typeName} left, {typeName} right) => left._value <= right._value;
        public static bool operator >=({typeName} left, {typeName} right) => left._value >= right._value;
        public static {typeName} Empty => default;
        public static {typeName} With(long value) => new(value);
    }}
}}";
                context.AddSource($"{typeName}.g", sourceText);
            }
            bool IsStronglyTypedId(ISymbol type)
            {
                return type.GetAttributes().Any(
                    y => y.AttributeClass != null && y.AttributeClass.Name.Contains(nameof(IdentifierAttribute)));
            }
        }
        private class SyntaxReceiver : ISyntaxReceiver
        {
            public List<StructDeclarationSyntax> CandidateDeclarations { get; } = new();
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                if (syntaxNode is not StructDeclarationSyntax declaration)
                {
                    return;
                }

                // Must be partial
                if (!declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
                    return;
                CandidateDeclarations.Add(declaration);
            }
        }
    }
}
