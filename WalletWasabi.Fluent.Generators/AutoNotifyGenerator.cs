using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace WalletWasabi.Fluent.Generators;

[Generator]
public class AutoNotifyGenerator : ISourceGenerator
{
	internal const string AutoNotifyAttributeDisplayString = "WalletWasabi.Fluent.AutoNotifyAttribute";

	private const string ReactiveObjectDisplayString = "ReactiveUI.ReactiveObject";

	private const string AttributeText = @"// <auto-generated />
#nullable enable
using System;

namespace WalletWasabi.Fluent
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    sealed class AutoNotifyAttribute : Attribute
    {
        public AutoNotifyAttribute()
        {
        }

        public string? PropertyName { get; set; }

		public AccessModifier SetterModifier { get; set; } = AccessModifier.Public;
    }
}";

	private const string ModifierText = @"// <auto-generated />

namespace WalletWasabi.Fluent
{
    public enum AccessModifier
    {
        None = 0,
        Public = 1,
        Protected = 2,
        Private = 3,
        Internal = 4
    }
}";

	public void Initialize(GeneratorInitializationContext context)
	{
		// System.Diagnostics.Debugger.Launch();
		context.RegisterForPostInitialization((i) =>
		{
			i.AddSource("AccessModifier.cs", SourceText.From(ModifierText, Encoding.UTF8));
			i.AddSource("AutoNotifyAttribute.cs", SourceText.From(AttributeText, Encoding.UTF8));
		});

		context.RegisterForSyntaxNotifications(() => new AutoNotifySyntaxReceiver());
	}

	public void Execute(GeneratorExecutionContext context)
	{
		if (context.SyntaxContextReceiver is not AutoNotifySyntaxReceiver receiver)
		{
			return;
		}

		var attributeSymbol = context.Compilation.GetTypeByMetadataName(AutoNotifyAttributeDisplayString);
		if (attributeSymbol is null)
		{
			return;
		}

		var notifySymbol = context.Compilation.GetTypeByMetadataName(ReactiveObjectDisplayString);
		if (notifySymbol is null)
		{
			return;
		}

		// TODO: https://github.com/dotnet/roslyn/issues/49385
#pragma warning disable RS1024
		var groupedFields = receiver.FieldSymbols.GroupBy(f => f.ContainingType);
#pragma warning restore RS1024

		foreach (var group in groupedFields)
		{
			var classSource = ProcessClass(group.Key, group.ToList(), attributeSymbol, notifySymbol);
			if (classSource is null)
			{
				continue;
			}
			context.AddSource($"{group.Key.Name}_AutoNotify.cs", SourceText.From(classSource, Encoding.UTF8));
		}
	}

	private static string? ProcessClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, INamedTypeSymbol notifySymbol)
	{
		if (!classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default))
		{
			return null;
		}

		string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

		var addNotifyInterface = !classSymbol.Interfaces.Contains(notifySymbol);
		var baseType = classSymbol.BaseType;
		while (true)
		{
			if (baseType is null)
			{
				break;
			}

			if (SymbolEqualityComparer.Default.Equals(baseType, notifySymbol))
			{
				addNotifyInterface = false;
				break;
			}

			baseType = baseType.BaseType;
		}

		var source = new StringBuilder();

		var format = new SymbolDisplayFormat(
			typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
			genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters | SymbolDisplayGenericsOptions.IncludeTypeConstraints | SymbolDisplayGenericsOptions.IncludeVariance);

		if (addNotifyInterface)
		{
			source.Append($@"// <auto-generated />
#nullable enable
using ReactiveUI;

namespace {namespaceName}
{{
    public partial class {classSymbol.ToDisplayString(format)} : {notifySymbol.ToDisplayString()}
    {{");
		}
		else
		{
			source.Append($@"// <auto-generated />
#nullable enable
using ReactiveUI;

namespace {namespaceName}
{{
    public partial class {classSymbol.ToDisplayString(format)}
    {{");
		}

		foreach (IFieldSymbol fieldSymbol in fields)
		{
			ProcessField(source, fieldSymbol, attributeSymbol);
		}

		source.Append($@"
    }}
}}");

		return source.ToString();
	}

	private static void ProcessField(StringBuilder source, IFieldSymbol fieldSymbol, ISymbol attributeSymbol)
	{
		var fieldName = fieldSymbol.Name;
		var fieldType = fieldSymbol.Type;
		var attributeData = fieldSymbol.GetAttributes().Single(ad => ad?.AttributeClass?.Equals(attributeSymbol, SymbolEqualityComparer.Default) ?? false);
		var overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;
		var propertyName = TypedStringHelpers.ChoosePropertyName(fieldName, overridenNameOpt);

		if (propertyName is null || propertyName.Length == 0 || propertyName == fieldName)
		{
			// Issue a diagnostic that we can't process this field.
			return;
		}

		var overridenSetterModifierOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "SetterModifier").Value;
		var setterModifier = ChooseSetterModifier(overridenSetterModifierOpt);
		if (setterModifier is null)
		{
			source.Append($@"
        public {fieldType} {propertyName}
        {{
            get => {fieldName};
        }}");
		}
		else
		{
			source.Append($@"
        public {fieldType} {propertyName}
        {{
            get => {fieldName};
            {setterModifier}set => this.RaiseAndSetIfChanged(ref {fieldName}, value);
        }}");
		}

		static string? ChooseSetterModifier(TypedConstant overridenSetterModifierOpt)
		{
			if (!overridenSetterModifierOpt.IsNull && overridenSetterModifierOpt.Value is not null)
			{
				var value = (int)overridenSetterModifierOpt.Value;
				return value switch
				{
					0 => null,// None
					1 => "",// Public
					2 => "protected ",// Protected
					3 => "private ",// Private
					4 => "internal ",// Internal
					_ => ""// Default
				};
			}
			else
			{
				return "";
			}
		}
	}
}
