﻿using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Flow.Launcher.Localization.SourceGenerators.Localize
{
    /// <summary>
	/// Generates properties for strings based on resource files.
	/// </summary>
    [Generator]
    public partial class LocalizeSourceGenerator : IIncrementalGenerator
    {
        #region Fields

        private const string CoreNamespace1 = "Flow.Launcher";
        private const string CoreNamespace2 = "Flow.Launcher.Core";
        private const string DefaultNamespace = "Flow.Launcher";
        private const string ClassName = "Localize";
        private const string PluginInterfaceName = "IPluginI18n";
        private const string PluginContextTypeName = "PluginInitContext";
        private const string XamlPrefix = "system";
        private const string XamlTag = "String";

        private readonly Regex _languagesXamlRegex = new Regex(@"\\Languages\\[^\\]+\.xaml$", RegexOptions.IgnoreCase);

        private static readonly Version PackageVersion = typeof(LocalizeSourceGenerator).Assembly.GetName().Version;

        #endregion

        #region Incremental Generator

        /// <summary>
        /// Initializes the generator and registers source output based on resource files.
        /// </summary>
        /// <param name="context">The initialization context.</param>
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var xamlFiles = context.AdditionalTextsProvider
                .Where(file => _languagesXamlRegex.IsMatch(file.Path));

            var localizedStrings = xamlFiles
                .Select((file, ct) => ParseXamlFile(file, ct))
                .Collect()
                .SelectMany((files, _) => files);

            // TODO: Add support for usedKeys
            var invocationKeys = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (n, _) => n is InvocationExpressionSyntax,
                    transform: GetLocalizationKeyFromInvocation)
                .Where(key => !string.IsNullOrEmpty(key))
                .Collect()
                .Select((keys, _) => keys.Distinct().ToImmutableHashSet());

            var pluginClasses = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (n, _) => n is ClassDeclarationSyntax,
                    transform: GetPluginClassInfo)
                .Where(info => info != null)
                .Collect();

            var compilation = context.CompilationProvider;

            var combined = localizedStrings.Combine(invocationKeys).Combine(pluginClasses).Combine(compilation).Combine(xamlFiles.Collect());

            context.RegisterSourceOutput(combined, Execute);
        }

        /// <summary>
        /// Executes the generation of string properties based on the provided data.
        /// </summary>
        /// <param name="spc">The source production context.</param>
        /// <param name="data">The provided data.</param>
        private void Execute(SourceProductionContext spc, 
            ((((ImmutableArray<LocalizableString> LocalizableStrings, 
            ImmutableHashSet<string> InvocationKeys), 
            ImmutableArray<PluginClassInfo> PluginClassInfos), 
            Compilation Compilation), 
            ImmutableArray<AdditionalText> AdditionalTexts) data)
        {
            var xamlFiles = data.AdditionalTexts;
            if (xamlFiles.Length == 0)
            {
                spc.ReportDiagnostic(Diagnostic.Create(
                    SourceGeneratorDiagnostics.CouldNotFindResourceDictionaries,
                    Location.None
                ));
                return;
            }

            var compilation = data.Item1.Compilation;
            var pluginClasses = data.Item1.Item1.PluginClassInfos;
            var usedKeys = data.Item1.Item1.Item1.InvocationKeys;
            var localizedStrings = data.Item1.Item1.Item1.LocalizableStrings;

            var assemblyName = compilation.AssemblyName ?? DefaultNamespace;
            var optimizationLevel = compilation.Options.OptimizationLevel;

            var pluginInfo = GetValidPluginInfo(pluginClasses, spc);
            var isCoreAssembly = assemblyName == CoreNamespace1 || assemblyName == CoreNamespace2;

            GenerateSource(
                spc,
                xamlFiles[0],
                localizedStrings,
                optimizationLevel,
                assemblyName,
                isCoreAssembly,
                pluginInfo,
                usedKeys);
        }

        #endregion

        #region Parse Xaml File

        private static ImmutableArray<LocalizableString> ParseXamlFile(AdditionalText file, CancellationToken ct)
        {
            var content = file.GetText(ct)?.ToString();
            if (content is null)
            {
                return ImmutableArray<LocalizableString>.Empty;
            }

            var doc = XDocument.Parse(content);
            var systemNs = doc.Root?.GetNamespaceOfPrefix(XamlPrefix); // Should be "system"
            var xNs = doc.Root?.GetNamespaceOfPrefix("x");
            if (systemNs is null || xNs is null)
            {
                return ImmutableArray<LocalizableString>.Empty;
            }

            var localizableStrings = new List<LocalizableString>();
            foreach (var element in doc.Descendants(systemNs + XamlTag)) // "String" elements in system namespace
            {
                if (ct.IsCancellationRequested)
                {
                    return ImmutableArray<LocalizableString>.Empty;
                }

                var key = element.Attribute(xNs + "Key")?.Value; // Correctly get x:Key
                var value = element.Value;
                var comment = element.PreviousNode as XComment;

                if (key != null)
                {
                    localizableStrings.Add(ParseLocalizableString(key, value, comment));
                }
            }

            return localizableStrings.ToImmutableArray();
        }

        private static LocalizableString ParseLocalizableString(string key, string value, XComment comment)
        {
            var (summary, parameters) = ParseComment(comment);
            return new LocalizableString(key, value, summary, parameters);
        }

        private static (string Summary, ImmutableArray<LocalizableStringParam> Parameters) ParseComment(XComment comment)
        {
            if (comment == null || comment.Value == null)
            {
                return (null, ImmutableArray<LocalizableStringParam>.Empty);
            }

            try
            {
                var doc = XDocument.Parse($"<root>{comment.Value}</root>");
                var summary = doc.Descendants("summary").FirstOrDefault()?.Value.Trim();
                var parameters = doc.Descendants("param")
                    .Select(p => new LocalizableStringParam(
                        int.Parse(p.Attribute("index").Value),
                        p.Attribute("name").Value,
                        p.Attribute("type").Value))
                    .ToImmutableArray();
                return (summary, parameters);
            }
            catch
            {
                return (null, ImmutableArray<LocalizableStringParam>.Empty);
            }
        }

        #endregion

        #region Get Used Localization Keys

        // TODO: Add support for usedKeys
        private static string GetLocalizationKeyFromInvocation(GeneratorSyntaxContext context, CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                return null;
            }

            var invocation = (InvocationExpressionSyntax)context.Node;
            var expression = invocation.Expression;
            var parts = new List<string>();

            // Traverse the member access hierarchy
            while (expression is MemberAccessExpressionSyntax memberAccess)
            {
                parts.Add(memberAccess.Name.Identifier.Text);
                expression = memberAccess.Expression;
            }

            // Add the leftmost identifier
            if (expression is IdentifierNameSyntax identifier)
            {
                parts.Add(identifier.Identifier.Text);
            }
            else
            {
                return null;
            }

            // Reverse to get [ClassName, SubClass, Method] from [Method, SubClass, ClassName]
            parts.Reverse();

            // Check if the first part is ClassName and there's at least one more part
            if (parts.Count < 2 || parts[0] != ClassName)
            {
                return null;
            }

            return parts[1];
        }

        #endregion

        #region Get Plugin Class Info

        private static PluginClassInfo GetPluginClassInfo(GeneratorSyntaxContext context, CancellationToken ct)
        {
            var classDecl = (ClassDeclarationSyntax)context.Node;
            var location = GetLocation(context.SemanticModel.SyntaxTree, classDecl);
            if (!classDecl.BaseList?.Types.Any(t => t.Type.ToString() == PluginInterfaceName) ?? true)
            {
                // Cannot find class that implements IPluginI18n
                return null;
            }

            var property = classDecl.Members
                .OfType<PropertyDeclarationSyntax>()
                .FirstOrDefault(p => p.Type.ToString() == PluginContextTypeName);
            if (property is null)
            {
                // Cannot find context
                return new PluginClassInfo(location, classDecl.Identifier.Text, null, false, false, false);
            }

            var modifiers = property.Modifiers;
            return new PluginClassInfo(
                location,
                classDecl.Identifier.Text,
                property.Identifier.Text,
                modifiers.Any(SyntaxKind.StaticKeyword),
                modifiers.Any(SyntaxKind.PrivateKeyword),
                modifiers.Any(SyntaxKind.ProtectedKeyword));
        }

        private static PluginClassInfo GetValidPluginInfo(
            ImmutableArray<PluginClassInfo> pluginClasses,
            SourceProductionContext context)
        {
            if (pluginClasses.All(p => p is null || p.PropertyName == null))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    SourceGeneratorDiagnostics.CouldNotFindPluginEntryClass,
                    Location.None
                ));
                return null;
            }

            foreach (var pluginClass in pluginClasses)
            {
                if (pluginClass == null || pluginClass.PropertyName is null)
                {
                    continue;
                }

                if (pluginClass.IsValid == true)
                {
                    return pluginClass;
                }

                if (!pluginClass.IsStatic)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SourceGeneratorDiagnostics.ContextPropertyNotStatic,
                        pluginClass.Location,
                        pluginClass.PropertyName
                    ));
                }

                if (pluginClass.IsPrivate)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SourceGeneratorDiagnostics.ContextPropertyIsPrivate,
                        pluginClass.Location,
                        pluginClass.PropertyName
                    ));
                }

                if (pluginClass.IsProtected)
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        SourceGeneratorDiagnostics.ContextPropertyIsProtected,
                        pluginClass.Location,
                        pluginClass.PropertyName
                    ));
                }
            }

            return null;
        }

        private static Location GetLocation(SyntaxTree syntaxTree, CSharpSyntaxNode classDeclaration)
        {
            return Location.Create(syntaxTree, classDeclaration.GetLocation().SourceSpan);
        }

        #endregion

        #region Generate Source

        private static void GenerateSource(
            SourceProductionContext spc,
            AdditionalText xamlFile,
            ImmutableArray<LocalizableString> localizedStrings,
            OptimizationLevel optimizationLevel,
            string assemblyName,
            bool isCoreAssembly,
            PluginClassInfo pluginInfo,
            IEnumerable<string> usedKeys)
        {
            // Get unusedKeys if we need to optimize
            IEnumerable<string> unusedKeys = new List<string>();
            if (optimizationLevel == OptimizationLevel.Release)
            {
                unusedKeys = localizedStrings
                    .Select(ls => ls.Key)
                    .ToImmutableHashSet()
                    .Except(usedKeys);

                foreach (var key in unusedKeys)
                {
                    spc.ReportDiagnostic(Diagnostic.Create(
                        SourceGeneratorDiagnostics.LocalizationKeyUnused,
                        Location.None,
                        key));
                }
            }

            var sourceBuilder = new StringBuilder();

            // Generate header
            GeneratedHeaderFromPath(sourceBuilder, xamlFile.Path);
            sourceBuilder.AppendLine();

            // Generate usings
            if (isCoreAssembly)
            {
                sourceBuilder.AppendLine("using Flow.Launcher.Core.Resource;");
                sourceBuilder.AppendLine();
            }

            // Generate nullable enable
            sourceBuilder.AppendLine("#nullable enable");
            sourceBuilder.AppendLine();

            // Generate namespace
            sourceBuilder.AppendLine($"namespace {assemblyName};");
            sourceBuilder.AppendLine();

            // Uncomment them for debugging
            //sourceBuilder.AppendLine("/*");
            /*// Generate all localization strings
            sourceBuilder.AppendLine("localizedStrings");
            foreach (var ls in localizedStrings)
            {
                sourceBuilder.AppendLine($"{ls.Key} - {ls.Value}");
            }
            sourceBuilder.AppendLine();

            // Generate all unused keys
            sourceBuilder.AppendLine("unusedKeys");
            foreach (var key in unusedKeys)
            {
                sourceBuilder.AppendLine($"{key}");
            }
            sourceBuilder.AppendLine();

            // Generate all used keys
            sourceBuilder.AppendLine("usedKeys");
            foreach (var key in usedKeys)
            {
                sourceBuilder.AppendLine($"{key}");
            }*/
            //sourceBuilder.AppendLine("*/");

            // Generate class
            sourceBuilder.AppendLine($"[System.CodeDom.Compiler.GeneratedCode(\"{nameof(LocalizeSourceGenerator)}\", \"{PackageVersion}\")]");
            sourceBuilder.AppendLine($"public static class {ClassName}");
            sourceBuilder.AppendLine("{");

            // Generate localization methods
            var tabString = Spacing(1);
            foreach (var ls in localizedStrings)
            {
                // TODO: Add support for usedKeys
                /*if (unusedKeys.Contains(ls.Key))
                {
                    continue;
                }*/

                GenerateDocComments(sourceBuilder, ls, tabString);
                GenerateLocalizationMethod(sourceBuilder, ls, isCoreAssembly, pluginInfo, tabString);
            }

            sourceBuilder.AppendLine("}");

            // Add source to context
            spc.AddSource($"{ClassName}.{assemblyName}.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        private static void GeneratedHeaderFromPath(StringBuilder sb, string xamlFilePath)
        {
            if (string.IsNullOrEmpty(xamlFilePath))
            {
                sb.AppendLine("/// <auto-generated/>");
            }
            else
            {
                sb.AppendLine("/// <auto-generated>")
                    .AppendLine($"/// From: {xamlFilePath}")
                    .AppendLine("/// </auto-generated>");
            }
        }

        private static void GenerateDocComments(StringBuilder sb, LocalizableString ls, string tabString)
        {
            if (ls.Summary != null)
            {
                sb.AppendLine($"{tabString}/// <summary>");
                foreach (var line in ls.Summary.Split('\n'))
                {
                    sb.AppendLine($"{tabString}/// {line.Trim()}");
                }
                sb.AppendLine($"{tabString}/// </summary>");
            }

            sb.AppendLine($"{tabString}/// <code>");
            foreach (var line in ls.Value.Split('\n'))
            {
                sb.AppendLine($"{tabString}/// {line.Trim()}");
            }
            sb.AppendLine($"{tabString}/// </code>");
        }

        private static void GenerateLocalizationMethod(
            StringBuilder sb,
            LocalizableString ls,
            bool isCoreAssembly,
            PluginClassInfo pluginInfo,
            string tabString)
        {
            sb.Append($"{tabString}public static string {ls.Key}(");
            var parameters = BuildParameters(ls);
            sb.Append(string.Join(", ", parameters.Select(p => $"{p.Type} {p.Name}")));
            sb.Append(") => ");

            var formatArgs = parameters.Count > 0
                ? $", {string.Join(", ", parameters.Select(p => p.Name))}"
                : string.Empty;

            if (isCoreAssembly)
            {
                sb.AppendLine(parameters.Count > 0
                    ? $"string.Format(InternationalizationManager.Instance.GetTranslation(\"{ls.Key}\"){formatArgs});"
                    : $"InternationalizationManager.Instance.GetTranslation(\"{ls.Key}\");");
            }
            else if (pluginInfo?.IsValid == true)
            {
                sb.AppendLine(parameters.Count > 0
                    ? $"string.Format({pluginInfo.ContextAccessor}.API.GetTranslation(\"{ls.Key}\"){formatArgs});"
                    : $"{pluginInfo.ContextAccessor}.API.GetTranslation(\"{ls.Key}\");");
            }
            else
            {
                sb.AppendLine("\"LOCALIZATION_ERROR\";");
            }

            sb.AppendLine();
        }

        private static List<MethodParameter> BuildParameters(LocalizableString ls)
        {
            var parameters = new List<MethodParameter>();
            for (var i = 0; i < 10; i++)
            {
                if (!ls.Value.Contains($"{{{i}}}"))
                {
                    continue;
                }

                var param = ls.Params.FirstOrDefault(p => p.Index == i);
                parameters.Add(param is null
                    ? new MethodParameter($"arg{i}", "object?")
                    : new MethodParameter(param.Name, param.Type));
            }
            return parameters;
        }

        private static string Spacing(int n)
        {
            Span<char> spaces = stackalloc char[n * 4];
            spaces.Fill(' ');

            var sb = new StringBuilder(n * 4);
            foreach (var c in spaces)
            {
                _ = sb.Append(c);
            }

            return sb.ToString();
        }

        #endregion

        #region Classes

        public class MethodParameter
        {
            public string Name { get; }
            public string Type { get; }

            public MethodParameter(string name, string type)
            {
                Name = name;
                Type = type;
            }
        }

        public class LocalizableStringParam
        {
            public int Index { get; }
            public string Name { get; }
            public string Type { get; }

            public LocalizableStringParam(int index, string name, string type)
            {
                Index = index;
                Name = name;
                Type = type;
            }
        }

        public class LocalizableString
        {
            public string Key { get; }
            public string Value { get; }
            public string Summary { get; }
            public IEnumerable<LocalizableStringParam> Params { get; }

            public LocalizableString(string key, string value, string summary, IEnumerable<LocalizableStringParam> @params)
            {
                Key = key;
                Value = value;
                Summary = summary;
                Params = @params;
            }
        }

        public class PluginClassInfo
        {
            public Location Location { get; }
            public string ClassName { get; }
            public string PropertyName { get; }
            public bool IsStatic { get; }
            public bool IsPrivate { get; }
            public bool IsProtected { get; }

            public string ContextAccessor => $"{ClassName}.{PropertyName}";
            public bool IsValid => PropertyName != null && IsStatic && (!IsPrivate) && (!IsProtected);

            public PluginClassInfo(Location location, string className, string propertyName, bool isStatic, bool isPrivate, bool isProtected)
            {
                Location = location;
                ClassName = className;
                PropertyName = propertyName;
                IsStatic = isStatic;
                IsPrivate = isPrivate;
                IsProtected = isProtected;
            }
        }

        #endregion
    }
}
