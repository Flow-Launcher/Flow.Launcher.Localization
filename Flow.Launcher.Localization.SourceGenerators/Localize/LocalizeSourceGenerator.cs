using System;
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

            var invocationKeys = context.SyntaxProvider
                .CreateSyntaxProvider(
                    predicate: (n, _) => n is InvocationExpressionSyntax,
                    transform: GetLocalizationKeyFromInvocation)
                .Where(key => !string.IsNullOrEmpty(key))
                .Collect()
                .Select((keys, _) => keys.ToImmutableHashSet());

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
            ImmutableHashSet<string> Strings), 
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

            var compilationData = data.Item1.Compilation;
            var pluginClassesList = data.Item1.Item1.PluginClassInfos;
            var usedKeys = data.Item1.Item1.Item1.Strings;
            var localizedStringsList = data.Item1.Item1.Item1.LocalizableStrings;

            var assemblyName = compilationData.AssemblyName ?? DefaultNamespace;
            var optimizationLevel = compilationData.Options.OptimizationLevel;

            var unusedKeys = localizedStringsList
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

            var pluginInfo = GetValidPluginInfo(pluginClassesList, spc);
            var isCoreAssembly = assemblyName == CoreNamespace1 || assemblyName == CoreNamespace2;

            GenerateSource(
                spc,
                localizedStringsList,
                unusedKeys,
                optimizationLevel,
                assemblyName,
                isCoreAssembly,
                pluginInfo);
        }

        #endregion

        #region Parse Xaml File

        private static ImmutableArray<LocalizableString> ParseXamlFile(AdditionalText file, CancellationToken ct)
        {
            var content = file.GetText(ct)?.ToString();
            if (content is null) return ImmutableArray<LocalizableString>.Empty;

            var doc = XDocument.Parse(content);
            var ns = doc.Root?.GetNamespaceOfPrefix(XamlPrefix);
            if (ns is null) return ImmutableArray<LocalizableString>.Empty;

            return doc.Descendants(ns + XamlTag)
                .Select(element =>
                {
                    var key = element.Attribute("Key")?.Value;
                    var value = element.Value;
                    var comment = element.PreviousNode as XComment;

                    return key is null ? null : ParseLocalizableString(key, value, comment);
                })
                .Where(ls => ls != null)
                .ToImmutableArray();
        }

        private static LocalizableString ParseLocalizableString(string key, string value, XComment comment)
        {
            var (summary, parameters) = ParseComment(comment);
            return new LocalizableString(key, value, summary, parameters);
        }

        private static (string Summary, ImmutableArray<LocalizableStringParam> Parameters) ParseComment(XComment comment)
        {
            if (comment == null || comment.Value == null)
                return (null, ImmutableArray<LocalizableStringParam>.Empty);

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

        #region Get Localization Keys

        private static string GetLocalizationKeyFromInvocation(GeneratorSyntaxContext context, CancellationToken ct)
        {
            var invocation = (InvocationExpressionSyntax)context.Node;
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (memberAccess.Expression is IdentifierNameSyntax identifierName && identifierName.Identifier.Text == ClassName)
                {
                    return memberAccess.Name.Identifier.Text;
                }
            }
            return null;
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
            SourceProductionContext context,
            ImmutableArray<LocalizableString> localizedStrings,
            IEnumerable<string> unusedKeys,
            OptimizationLevel optimizationLevel,
            string assemblyName,
            bool isCoreAssembly,
            PluginClassInfo pluginInfo)
        {
            var sourceBuilder = new StringBuilder();
            sourceBuilder.AppendLine("// <auto-generated />");
            sourceBuilder.AppendLine("#nullable enable");

            if (isCoreAssembly)
            {
                sourceBuilder.AppendLine("using Flow.Launcher.Core.Resource;");
            }

            sourceBuilder.AppendLine($"namespace {assemblyName};");
            sourceBuilder.AppendLine();
            sourceBuilder.AppendLine($"[System.CodeDom.Compiler.GeneratedCode(\"{nameof(LocalizeSourceGenerator)}\", \"1.0.0\")]");
            sourceBuilder.AppendLine($"public static class {ClassName}");
            sourceBuilder.AppendLine("{");

            foreach (var ls in localizedStrings)
            {
                if (optimizationLevel == OptimizationLevel.Release && unusedKeys.Contains(ls.Key))
                    continue;

                GenerateDocComments(sourceBuilder, ls);
                GenerateLocalizationMethod(sourceBuilder, ls, isCoreAssembly, pluginInfo);
            }

            sourceBuilder.AppendLine("}");

            context.AddSource($"{ClassName}.g.cs", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        private static void GenerateDocComments(StringBuilder sb, LocalizableString ls)
        {
            if (ls.Summary != null)
            {
                sb.AppendLine("/// <summary>");
                foreach (var line in ls.Summary.Split('\n'))
                    sb.AppendLine($"/// {line.Trim()}");
                sb.AppendLine("/// </summary>");
            }

            sb.AppendLine("/// <code>");
            foreach (var line in ls.Value.Split('\n'))
                sb.AppendLine($"/// {line.Trim()}");
            sb.AppendLine("/// </code>");
        }

        private static void GenerateLocalizationMethod(
            StringBuilder sb,
            LocalizableString ls,
            bool isCoreAssembly,
            PluginClassInfo pluginInfo)
        {
            sb.Append($"public static string {ls.Key}(");
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
                if (!ls.Value.Contains($"{{{i}}}")) continue;

                var param = ls.Params.FirstOrDefault(p => p.Index == i);
                parameters.Add(param is null
                    ? new MethodParameter($"arg{i}", "object?")
                    : new MethodParameter(param.Name, param.Type));
            }
            return parameters;
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
