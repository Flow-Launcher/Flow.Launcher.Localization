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

        public static readonly Regex LanguagesXamlRegex = new Regex(@"\\Languages\\[^\\]+\.xaml$", RegexOptions.IgnoreCase);
    }
}
