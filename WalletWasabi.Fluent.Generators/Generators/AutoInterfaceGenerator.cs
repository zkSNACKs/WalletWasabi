using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Generic;
using System.Linq;

namespace WalletWasabi.Fluent.Generators;

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
					property.SetMethod is { }
					? "{ get; set; }"
					: "{ get; }";

				var type = property.Type.SimplifyType(namespaces);
				properties.Add($"    {type} {property.Name} {accessors}");
			}
			else if (member is IMethodSymbol method && method.MethodKind == MethodKind.Ordinary)
			{
				var returnType = method.ReturnType.SimplifyType(namespaces);

				var signature = $"    ";

				signature += returnType;

				signature += $" {method.Name}";
				if (method.IsGenericMethod)
				{
					signature += "<";

					var typeArgs =
						from argument in method.TypeArguments
						let typeName = argument.SimplifyType(namespaces)
						select typeName;

					signature += string.Join(", ", typeArgs);

					signature += ">";
				}

				signature += "(";

				var parameters =
					from parameter in method.Parameters
					let type = parameter.Type.SimplifyType(namespaces)
					let name = parameter.Name
					let defaultValue = parameter.GetExplicitDefaultValueString()
					let defaultValueString = defaultValue != null ? " = " + defaultValue : null
					select $"{type} {name}{defaultValueString}";

				signature += string.Join(", ", parameters);

				signature += ");";

				methods.Add(signature);
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
