using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WalletWasabi.Fluent.Generators.Abstractions;

namespace WalletWasabi.Fluent.Generators.Generators;

internal class AutoInterfaceGenerator : GeneratorStep<ClassDeclarationSyntax>
{
	private const string AutoInterfaceAttribute = "WalletWasabi.Fluent.AutoInterfaceAttribute";

	public override void Execute(ClassDeclarationSyntax[] syntaxNodes)
	{
		foreach (var cls in syntaxNodes)
		{
			Execute(cls);
		}
	}

	private void Execute(ClassDeclarationSyntax cls)
	{
		var semanticModel = GetSemanticModel(cls.SyntaxTree);

		if (semanticModel.GetDeclaredSymbol(cls) is not INamedTypeSymbol namedTypeSymbol)
		{
			return;
		}

		var hasAutoInterfaceAttribute =
			cls.AttributeLists
			   .Any(al => al.Attributes.Any(attr => semanticModel.GetTypeInfo(attr).Type?.ToDisplayString() == AutoInterfaceAttribute));

		if (!hasAutoInterfaceAttribute)
		{
			return;
		}

		var className = namedTypeSymbol.Name;
		var interfaceNamespace = namedTypeSymbol.ContainingNamespace.ToDisplayString();
		var interfaceName = $"I{namedTypeSymbol.Name}";

		var members =
			namedTypeSymbol.GetMembers()
						   .Where(x => x.DeclaredAccessibility == Accessibility.Public)
						   .Where(x => !x.IsStatic)
						   .ToList();

		var namespaces = new List<string>();
		var properties = new List<string>();
		var methods = new List<string>();
		foreach (var member in members)
		{
			if (member is IPropertySymbol property)
			{
				var accessors =
					property.SetMethod switch
					{
						IMethodSymbol s when s.IsInitOnly => "{ get; init; }",
						IMethodSymbol s                   => "{ get; set; }",
						_                                 => "{ get; }"
					};

				var type = property.Type.SimplifyType(namespaces);
				properties.Add($"\t{type} {property.Name} {accessors}");
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				var returnType = method.ReturnType.SimplifyType(namespaces);

				StringBuilder signatureBuilder = new(capacity: 150);
				signatureBuilder.Append('\t');
				signatureBuilder.Append(returnType);
				signatureBuilder.Append(' ');
				signatureBuilder.Append(method.Name);

				if (method.IsGenericMethod)
				{
					signatureBuilder.Append('<');

					var typeArgs =
						from argument in method.TypeArguments
						let typeName = argument.SimplifyType(namespaces)
						select typeName;

					signatureBuilder.Append(string.Join(", ", typeArgs));
					signatureBuilder.Append('>');
				}

				signatureBuilder.Append('(');

				var parameters =
					from parameter in method.Parameters
					let declaration = parameter.DeclaringSyntaxReferences.First().GetSyntax()
					let attributeList = declaration.DescendantNodes().OfType<AttributeListSyntax>().FirstOrDefault()
					let attributeTypes = parameter.GetAttributes().Select(attr => attr.AttributeClass?.SimplifyType(namespaces)).ToList()
					let refKind = parameter.RefKind == RefKind.Out ? "out " : ""
					let type = parameter.Type.SimplifyType(namespaces)
					let name = parameter.Name
					let defaultValue = parameter.GetExplicitDefaultValueString()
					let defaultValueString = defaultValue != null ? " = " + defaultValue : null
					select $"{attributeList?.ToFullString()}{refKind}{type} {name}{defaultValueString}";

				signatureBuilder.Append(string.Join(", ", parameters));
				signatureBuilder.Append(");");

				methods.Add(signatureBuilder.ToString());
			}
		}

		namespaces =
			namespaces.Distinct()
					  .OrderBy(x => x)
					  .Where(x => x != interfaceNamespace)
					  .Select(x => $"using {x};")
					  .ToList();

		var source =
			$$"""
			// <auto-generated />

			#nullable enable
			{{string.Join("\r\n", namespaces)}}

			namespace {{interfaceNamespace}};

			public partial class {{className}}: {{interfaceName}}
			{
			}

			public partial interface {{interfaceName}}
			{
			{{string.Join("\r\n\r\n", properties)}}

			{{string.Join("\r\n\r\n", methods)}}
			}
			""";

		AddSource($"{className}.AutoInterface.g.cs", source);
	}
}
