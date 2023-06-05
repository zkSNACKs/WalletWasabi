using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WalletWasabi.Fluent.Generators;

[Generator]
public class NavigationMetaDataGenerator : ISourceGenerator
{
	public const string NavigationMetaDataAttributeDisplayString = "WalletWasabi.Fluent.NavigationMetaDataAttribute";

	private const string NavigationMetaDataDisplayString = "WalletWasabi.Fluent.NavigationMetaData";

	private const string RoutableViewModelDisplayString = "WalletWasabi.Fluent.ViewModels.Navigation.RoutableViewModel";

	public void Initialize(GeneratorInitializationContext context)
	{
		context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
	}

	public void Execute(GeneratorExecutionContext context)
	{
		if (context.SyntaxContextReceiver is not SyntaxReceiver receiver)
		{
			return;
		}

		var attributeSymbol = context.Compilation.GetTypeByMetadataName(NavigationMetaDataAttributeDisplayString);
		if (attributeSymbol is null)
		{
			return;
		}

		var metadataSymbol = context.Compilation.GetTypeByMetadataName(NavigationMetaDataDisplayString);
		if (metadataSymbol is null)
		{
			return;
		}

		foreach (var namedTypeSymbol in receiver.NamedTypeSymbols)
		{
			var classSource = ProcessClass(context.Compilation, namedTypeSymbol, attributeSymbol, metadataSymbol);
			if (classSource is not null)
			{
				context.AddSource($"{namedTypeSymbol.Name}_NavigationMetaData.cs", SourceText.From(classSource, Encoding.UTF8));
			}
		}
	}

	private static string? ProcessClass(Compilation compilation, INamedTypeSymbol classSymbol, ISymbol attributeSymbol, ISymbol metadataSymbol)
	{
		if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
		{
			return null;
		}

		string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

		var format = new SymbolDisplayFormat(
			typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
			genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance);

		var attributeData = classSymbol
			.GetAttributes()
			.Single(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false);

		var isNavBarItem =
			attributeData.NamedArguments.Any(x => x.Key == "NavBarPosition") &&
			attributeData.NamedArguments.Any(x => x.Key == "NavBarSelectionMode");

		var implementedInterfaces = new List<string>();

		if (isNavBarItem)
		{
			var navBarSelectionMode = attributeData.NamedArguments.First(x => x.Key == "NavBarSelectionMode").Value.Value;
			if (navBarSelectionMode is int s)
			{
				if (s == 1)
				{
					implementedInterfaces.Add("INavBarButton");
				}
				else if (s == 2)
				{
					implementedInterfaces.Add("INavBarToggle");
				}
			}
		}

		var implementedInterfacesString =
			implementedInterfaces.Any()
			? ": " + string.Join(", ", implementedInterfaces)
			: "";

		var source = new StringBuilder($@"// <auto-generated />
#nullable enable
using System;
using System.Threading.Tasks;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace {namespaceName}
{{
    public partial class {classSymbol.ToDisplayString(format)}{implementedInterfacesString}
    {{
");

		source.Append($@"        public static {metadataSymbol.ToDisplayString()} MetaData {{ get; }} = new()
        {{
");
		var length = attributeData.NamedArguments.Length;
		for (int i = 0; i < length; i++)
		{
			var namedArgument = attributeData.NamedArguments[i];

			source.AppendLine($"            {namedArgument.Key} = " +
							  $"{(namedArgument.Value.Kind == TypedConstantKind.Array ? "new[] " : "")}" +
							  $"{namedArgument.Value.ToCSharpString()}{(i < length - 1 ? "," : "")}");
		}

		source.Append($@"        }};
");

		source.AppendLine($@"        public static void RegisterAsyncLazy(Func<Task<RoutableViewModel?>> createInstance) => NavigationManager.RegisterAsyncLazy(MetaData, createInstance);");
		source.AppendLine($@"        public static void RegisterLazy(Func<RoutableViewModel?> createInstance) => NavigationManager.RegisterLazy(MetaData, createInstance);");
		source.AppendLine($@"        public static void Register(RoutableViewModel createInstance) => NavigationManager.Register(MetaData, createInstance);");

		var routeableClass = compilation.GetTypeByMetadataName(RoutableViewModelDisplayString);

		if (routeableClass is { })
		{
			bool addRouteableMetaData = false;
			var baseType = classSymbol.BaseType;
			while (true)
			{
				if (baseType is null)
				{
					break;
				}

				if (SymbolEqualityComparer.Default.Equals(baseType, routeableClass))
				{
					addRouteableMetaData = true;
					break;
				}

				baseType = baseType.BaseType;
			}

			if (addRouteableMetaData)
			{
				if (attributeData.NamedArguments.Any(x => x.Key == "NavigationTarget"))
				{
					source.AppendLine(
						$@"        public override NavigationTarget DefaultTarget => MetaData.NavigationTarget;");
				}

				if (attributeData.NamedArguments.Any(x => x.Key == "Title"))
				{
					source.AppendLine($@"        public override string Title {{ get => MetaData.Title!; protected set {{}} }} ");
				}
			}

			if (attributeData.NamedArguments.Any(x => x.Key == "IconName"))
			{
				source.AppendLine($@"        public string IconName => MetaData.IconName!;");
			}

			if (attributeData.NamedArguments.Any(x => x.Key == "IconNameFocused"))
			{
				source.AppendLine($@"        public string IconNameFocused => MetaData.IconNameFocused!;");
			}
		}

		source.Append($@"    }}
}}");

		return source.ToString();
	}

	private class SyntaxReceiver : ISyntaxContextReceiver
	{
		public List<INamedTypeSymbol> NamedTypeSymbols { get; } = new();

		public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
		{
			if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
				&& classDeclarationSyntax.AttributeLists.Count > 0)
			{
				var namedTypeSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax);
				if (namedTypeSymbol is null)
				{
					return;
				}

				var attributes = namedTypeSymbol.GetAttributes();
				if (attributes.Any(ad => ad?.AttributeClass?.ToDisplayString() == NavigationMetaDataAttributeDisplayString))
				{
					NamedTypeSymbols.Add(namedTypeSymbol);
				}
			}
		}
	}
}
