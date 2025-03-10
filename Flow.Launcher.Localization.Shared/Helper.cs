using Microsoft.CodeAnalysis.Diagnostics;

namespace Flow.Launcher.Localization.Shared
{
    public static class Helper
    {
        public static bool GetFLLUseDependencyInjection(this AnalyzerConfigOptionsProvider configOptions)
        {
            if (!configOptions.GlobalOptions.TryGetValue("build_property.FLLUseDependencyInjection", out var result) ||
                !bool.TryParse(result, out var useDI))
            {
                return false; // Default to false
            }
            return useDI;
        }
    }
}
