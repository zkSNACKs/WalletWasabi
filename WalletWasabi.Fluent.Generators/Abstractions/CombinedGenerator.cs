using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WalletWasabi.Fluent.Generators.Abstractions;

internal abstract class CombinedGenerator : ISourceGenerator
{
	private List<Func<GeneratorStep>> StepFactories { get; } = new();
	private List<StaticFileGenerator> StaticFileGenerators { get; } = new();

	public void Initialize(GeneratorInitializationContext context)
	{
		var files =
			StaticFileGenerators.SelectMany(x => x.Generate())
								.ToArray();

		if (files.Length != 0)
		{
			context.RegisterForPostInitialization(ctx =>
			{
				foreach (var (fileName, source) in files)
				{
					ctx.AddSource($"{Guid.NewGuid()}_{fileName}", SourceText.From(source, Encoding.UTF8));
				}
			});
		}

		if (StepFactories.Count != 0)
		{
			context.RegisterForSyntaxNotifications(() => new CombinedSyntaxReceiver(this));
		}
	}

	public void Execute(GeneratorExecutionContext context)
	{
		if (context.SyntaxReceiver is not CombinedSyntaxReceiver receiver)
		{
			return;
		}

		var compilation = context.Compilation;

		foreach (var step in receiver.Steps)
		{
			step.Initialize(context, compilation);
			step.OnInitialize(compilation, receiver.Steps);
			step.Execute();

			// This is the core part of CombinedGenerator.
			// Each step creates a new Compilation, containing additional syntax trees
			// The CombinedGenerator passes the new Compilation (with the added Syntax Trees) from step to step
			// in order to be able to semantically analyze the types declared inside those syntax trees
			// otherwise SemanticModel has no information about those types and therefore it returns null or errored out type symbols
			// for types declared in source generated files.
			compilation = step.Context.Compilation;
		}
	}

	protected void Add<T>() where T : GeneratorStep, new()
	{
		StepFactories.Add(() => new T());
	}

	protected void AddStaticFileGenerator<T>() where T : StaticFileGenerator, new()
	{
		StaticFileGenerators.Add(new T());
	}

	private class CombinedSyntaxReceiver : ISyntaxReceiver
	{
		public CombinedSyntaxReceiver(CombinedGenerator generator)
		{
			Steps =
				generator.StepFactories
						 .Select(x => x())
						 .ToArray();
		}

		public GeneratorStep[] Steps { get; }

		public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
		{
			foreach (var step in Steps)
			{
				if (step is ISyntaxReceiver receiver)
				{
					receiver.OnVisitSyntaxNode(syntaxNode);
				}
			}
		}
	}
}
