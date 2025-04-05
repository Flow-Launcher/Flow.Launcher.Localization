﻿using System;
using System.Text;
using Flow.Launcher.Localization.Shared;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Flow.Launcher.Localization.SourceGenerators.Localize
{
    [Generator]
    public partial class PublicApiSourceGenerator : IIncrementalGenerator
    {
        #region Fields

        private static readonly Version PackageVersion = typeof(PublicApiSourceGenerator).Assembly.GetName().Version;

        #endregion

        #region Incremental Generator

        /// <summary>
        /// Initializes the generator and registers source output based on build property FLLUseDependencyInjection.
        /// </summary>
        /// <param name="context">The initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var compilation = context.CompilationProvider;

            var configOptions = context.AnalyzerConfigOptionsProvider;

            var compilationEnums = configOptions.Combine(compilation);

            context.RegisterSourceOutput(compilationEnums, Execute);
        }

        /// <summary>
        /// Executes the generation of public api property based on the provided data.
        /// </summary>
        /// <param name="spc">The source production context.</param>
        /// <param name="data">The provided data.</param>
        private void Execute(SourceProductionContext spc,
            (AnalyzerConfigOptionsProvider ConfigOptionsProvider, Compilation Compilation) data)
        {
            var compilation = data.Compilation;
            var configOptions = data.ConfigOptionsProvider;

            var assemblyNamespace = compilation.AssemblyName ?? Constants.DefaultNamespace;
            var useDI = configOptions.GetFLLUseDependencyInjection();

            // If we do not use dependency injection, we do not need to generate the public api property
            if (!useDI) return;

            GenerateSource(spc, assemblyNamespace);
        }

        #endregion

        #region Generate Source

        private void GenerateSource(
            SourceProductionContext spc,
            string assemblyNamespace)
        {
            var tabString = Helper.Spacing(1);

            var sourceBuilder = new StringBuilder();

            // Generate header
            GeneratedHeaderFromPath(sourceBuilder);
            sourceBuilder.AppendLine();

            // Generate nullable enable
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();

            // Generate namespace
            sourceBuilder.AppendLine($"namespace {assemblyNamespace};");
            sourceBuilder.AppendLine();

            // Generate class
            sourceBuilder.AppendLine($"[System.CodeDom.Compiler.GeneratedCode(\"{nameof(PublicApiSourceGenerator)}\", \"{PackageVersion}\")]");
            sourceBuilder.AppendLine($"internal static class {Constants.PublicApiClassName}");
            sourceBuilder.AppendLine("{");

            // Generate properties
            sourceBuilder.AppendLine($"{tabString}private static Flow.Launcher.Plugin.IPublicAPI? {Constants.PublicApiPrivatePropertyName} = null;");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"{tabString}/// <summary>");
            sourceBuilder.AppendLine($"{tabString}/// Get <see cref=\"Flow.Launcher.Plugin.IPublicAPI\"> instance");
            sourceBuilder.AppendLine($"{tabString}/// </summary>");
            sourceBuilder.AppendLine($"{tabString}internal static Flow.Launcher.Plugin.IPublicAPI {Constants.PublicApiInternalPropertyName} =>" +
                $"{Constants.PublicApiPrivatePropertyName} ??= CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Flow.Launcher.Plugin.IPublicAPI>();");
            sourceBuilder.AppendLine($"}}");

            // Add source to context
            spc.AddSource($"{Constants.PublicApiClassName}.{assemblyNamespace}.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        private static void GeneratedHeaderFromPath(StringBuilder sb)
        {
            sb.AppendLine("/// <auto-generated/>");
        }

        #endregion
    }
}
