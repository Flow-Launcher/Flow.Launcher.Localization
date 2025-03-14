﻿using System.Text.RegularExpressions;

namespace Flow.Launcher.Localization.Shared
{
    public static class Constants
    {
        public const string DefaultNamespace = "Flow.Launcher";
        public const string ClassName = "Localize";
        public const string PluginInterfaceName = "IPluginI18n";
        public const string PluginContextTypeName = "PluginInitContext";
        public const string SystemPrefixUri = "clr-namespace:System;assembly=mscorlib";
        public const string XamlPrefixUri = "http://schemas.microsoft.com/winfx/2006/xaml";
        public const string XamlTag = "String";
        public const string KeyAttribute = "Key";
        public const string SummaryElementName = "summary";
        public const string ParamElementName = "param";
        public const string IndexAttribute = "index";
        public const string NameAttribute = "name";
        public const string TypeAttribute = "type";
        public const string OldLocalizationMethodName = "GetTranslation";
        public const string StringFormatMethodName = "Format";
        public const string StringFormatTypeName = "string";

        public static readonly Regex LanguagesXamlRegex = new Regex(@"\\Languages\\[^\\]+\.xaml$", RegexOptions.IgnoreCase);
        public static readonly string[] OldLocalizationClasses = { "IPublicAPI", "Internationalization" };
    }
}
