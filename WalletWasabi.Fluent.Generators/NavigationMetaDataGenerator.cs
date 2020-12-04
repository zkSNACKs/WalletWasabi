using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace WalletWasabi.Fluent.Generators
{
	[Generator]
	public class NavigationMetaDataGenerator : ISourceGenerator
	{
		#region AttributeText

		private const string AttributeText = @"// <auto-generated />
using System;

namespace WalletWasabi.Fluent
{
	public enum NavBarPosition
	{
		None,
		Top,
		Bottom
	}

	public enum NavigationTarget
	{
		Default = 0,
		HomeScreen = 1,
		DialogScreen = 2,
		DialogHost = 3
	}

	public sealed class NavigationMetaData
	{
		public bool Searchable { get; init; } = true;

		public string Title { get; init; }

		public string Caption { get; init; }

		public string IconName { get; init; }

		public int Order { get; init; }

		public string Category { get; init; }

		public string[] Keywords { get; init; }

		public string[] XamlKeywords { get; init; }

		public NavBarPosition NavBarPosition { get; init; }

		public NavigationTarget NavigationTarget { get; init; }
	}

	[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
	public sealed class NavigationMetaDataAttribute : Attribute
	{
		public NavigationMetaDataAttribute()
		{
		}

		public bool Searchable { get; set; }

		public string Title { get; set; }

		public string Caption { get; set; }

		public string IconName { get; set; }

		public int Order { get; set; }

		public string Category { get; set; }

		public string[] Keywords { get; set; }

		public NavBarPosition NavBarPosition {get; set; }

		public NavigationTarget NavigationTarget { get; set; }
	}
}";

		#endregion

		public void Initialize(GeneratorInitializationContext context)
		{
			// System.Diagnostics.Debugger.Launch();
			context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
		}

		public void Execute(GeneratorExecutionContext context)
		{
			context.AddSource("NavigationMetaDataAttribute", SourceText.From(AttributeText, Encoding.UTF8));

			if (!(context.SyntaxReceiver is SyntaxReceiver receiver))
			{
				return;
			}

			var options = (context.Compilation as CSharpCompilation)?.SyntaxTrees[0].Options as CSharpParseOptions;
			var compilation = context.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SourceText.From(AttributeText, Encoding.UTF8), options));

			var attributeSymbol = compilation.GetTypeByMetadataName("WalletWasabi.Fluent.NavigationMetaDataAttribute");
			if (attributeSymbol is null)
			{
				return;
			}

			var metadataSymbol = compilation.GetTypeByMetadataName("WalletWasabi.Fluent.NavigationMetaData");
			if (metadataSymbol is null)
			{
				return;
			}

			List<INamedTypeSymbol> namedTypeSymbols = new();

			foreach (var candidateClass in receiver.CandidateClasses)
			{
				var semanticModel = compilation.GetSemanticModel(candidateClass.SyntaxTree);
				var namedTypeSymbol = semanticModel.GetDeclaredSymbol(candidateClass);
				if (namedTypeSymbol is null)
				{
					continue;
				}

				var attributes = namedTypeSymbol.GetAttributes();
				if (attributes.Any(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false))
				{
					namedTypeSymbols.Add(namedTypeSymbol);
				}
			}

			foreach (var namedTypeSymbol in namedTypeSymbols)
			{
				var classSource = ProcessClass(compilation, namedTypeSymbol, attributeSymbol, metadataSymbol, context);
				if (classSource is not null)
				{
					context.AddSource($"{namedTypeSymbol.Name}_NavigationMetaData.cs", SourceText.From(classSource, Encoding.UTF8));
				}
			}
		}

		private static string? ProcessClass(Compilation compilation, INamedTypeSymbol classSymbol, ISymbol attributeSymbol, ISymbol metadataSymbol, GeneratorExecutionContext context)
		{
			if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace,
				SymbolEqualityComparer.Default))
			{
				return null;
			}

			string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            var format = new SymbolDisplayFormat(
                typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
                genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance
            );

			var source = new StringBuilder($@"// <auto-generated />
#nullable enable
using System;
using System.Threading.Tasks;
using WalletWasabi.Fluent.ViewModels.Navigation;

namespace {namespaceName}
{{
    public partial class {classSymbol.ToDisplayString(format)}
    {{
");

			var attributeData = classSymbol
				.GetAttributes()
				.Single(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false);

			source.Append($@"        public static {metadataSymbol.ToDisplayString()} MetaData {{ get; }} = new()
        {{
");

			var xamlKeywords = GetXamlKeywords(classSymbol.Name, context, namespaceName)
				.Distinct(StringComparer.InvariantCultureIgnoreCase).ToList();

			source.Append($"            XamlKeywords = new{(xamlKeywords.Count == 0 ? " string" : "")}[] {{ ");
			for (var i = 0; i < xamlKeywords.Count; i++)
			{
				var keyword = xamlKeywords[i];
				source.Append($"\"{keyword}\"{(i < xamlKeywords.Count - 1 ? ", " : " ")}");
			}
			source.AppendLine("},");

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

			source.AppendLine($@"        public static void RegisterAsyncLazy(Func<Task<RoutableViewModel>> createInstance) => NavigationManager.RegisterAsyncLazy(MetaData, createInstance);");
			source.AppendLine($@"        public static void RegisterLazy(Func<RoutableViewModel> createInstance) => NavigationManager.RegisterLazy(MetaData, createInstance);");
			source.AppendLine($@"        public static void Register(RoutableViewModel createInstance) => NavigationManager.Register(MetaData, createInstance);");

			var routeableClass = compilation.GetTypeByMetadataName("WalletWasabi.Fluent.ViewModels.Navigation.RoutableViewModel");

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

					if (attributeData.NamedArguments.Any(x => x.Key == "IconName"))
					{
						source.AppendLine($@"        public override string IconName => MetaData.IconName;");
					}
				}
			}

			source.Append($@"    }}
}}");

			return source.ToString();
		}

		private static IEnumerable<string> GetXamlKeywords(string className, GeneratorExecutionContext context, string namespaceName)
		{
			var keywords = new List<string>();
			var viewName = $"{namespaceName}.{className}".Replace("ViewModel", "View");
			var xamlFileName = $"{viewName}.xaml";
			var aXamlFileName = $"{viewName}.axaml";
			var directorySeparatorChar = Path.DirectorySeparatorChar.ToString();
			var xamlFile = context
				.AdditionalFiles
				.FirstOrDefault(text =>
				{
					var str = text.Path.Replace(directorySeparatorChar, ".");
					return str.EndsWith(xamlFileName) || str.EndsWith(aXamlFileName);
				});
			if (xamlFile is null)
			{
				yield break;
			}

			var xaml = xamlFile.GetText()!.ToString();
			var doc = XDocument.Parse(xaml);

			var textBlocks = doc.Descendants().Where(x => x.Name.LocalName == "TextBlock");
			
			foreach (var textBlock in textBlocks)
			{
				var text = textBlock.Attributes().FirstOrDefault(x => x.Name.LocalName == "Text");
				if (text?.Value is { } value)
				{
					var trimmed = value.Trim();
					if (!trimmed.StartsWith("{"))
					{
						yield return value;
					}
				}
			}
		}

		private class SyntaxReceiver : ISyntaxReceiver
		{
			public List<ClassDeclarationSyntax> CandidateClasses { get; } = new();

			public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
			{
				if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax
				    && classDeclarationSyntax.AttributeLists.Count > 0)
				{
					CandidateClasses.Add(classDeclarationSyntax);
				}
			}
		}
	}
}