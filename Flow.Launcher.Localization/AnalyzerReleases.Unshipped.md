; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
FLAN0001 | Localization | Warning | FLAN0001_OldLocalizationApiUsed
FLAN0002 | Localization | Error | FLAN0002_ContextIsAField
FLAN0003 | Localization | Error | FLAN0003_ContextIsNotStatic
FLAN0004 | Localization | Error | FLAN0004_ContextAccessIsTooRestrictive
FLAN0005 | Localization | Error | FLAN0005_ContextIsNotDeclared
FLSG0001 | Localization | Warning | FLSG0001_CouldNotFindResourceDictionaries
FLSG0002 | Localization | Warning | FLSG0002_CouldNotFindPluginEntryClass
FLSG0003 | Localization | Warning | FLSG0003_CouldNotFindContextProperty
FLSG0004 | Localization | Warning | FLSG0004_ContextPropertyNotStatic
FLSG0005 | Localization | Warning | FLSG0005_ContextPropertyIsPrivate
FLSG0006 | Localization | Warning | FLSG0006_ContextPropertyIsProtected
FLSG0007 | Localization | Warning | FLSG0007_LocalizationKeyUnused
