Localization toolkit is for Flow C# plugin developers to improve their localization experience.

## Initialization

For C# Plugins, we need to install and reference [Flow.Launcher.Localization](www.nuget.org/packages/Flow.Launcher.Localization) by Nuget.

## Build properties

### `FLLUseDependencyInjection`

Whether to use depenedency injection to get `IPublicAPI` instance. Default by false.

If set to `false`, the `Main` class that implements **[IPlugin](/API-Reference/Flow.Launcher.Plugin/IPlugin.md)** or **[IAsyncPlugin](/API-Reference/Flow.Launcher.Plugin/IAsyncPlugin.md)** must have a [PluginInitContext](/API-Reference/Flow.Launcher.Plugin/PluginInitContext.md) property which must be at least `internal static`.

If set to `true`, we can access `IPublicAPI` instance from `PublicApi.Instance` in the project by dependency injection.
And the `Main` class does not need to have a [PluginInitContext](/API-Reference/Flow.Launcher.Plugin/PluginInitContext.md) property.
(Not recommended for plugin projects because this will make plugins only compabible with Flow 1.20.0 or higher)

## Usage

### Main class

`Main` class must implement [IPluginI18n](/API-Reference/Flow.Launcher.Plugin/IPluginI18n.md).

If `FLLUseDependencyInjection` is set to `false`, `Main` class must have a [PluginInitContext](/API-Reference/Flow.Launcher.Plugin/PluginInitContext.md) property like:

```
public class Main : IPlugin, IPluginI18n // Must implement IPluginI18n
{
    internal static PluginInitContext Context { get; private set; } = null!; // At least internal static property
    private static IPublicAPI API => Context.API;

    ...
}
```

### Localized strings

With this toolkit, we can replace `Context.API.GetTranslation("flowlauncher_plugin_localization_demo_plugin_name")` with `Localize.flowlauncher_plugin_localization_demo_plugin_name()`.

And we can also replace `string.Format(Context.API.GetTranslation("flowlauncher_plugin_localization_demo_plugin_used"), string.Empty, null, string.Empty)` with `Localize.flowlauncher_plugin_localization_demo_plugin_used(string.Empty, null, string.Empty)`.

### Localized enums

If you have enum types like `DemoEnum` that needs localization in displaying them on a combo box control. You can add `EnumLocalize` attribute to enable localiztion support.
For all fields in this `EnumType`, if you want to specific one localization key for this field, you can use `EnumLocalizeKey` attribute; or if you want to specific one constant value for this field, you can use `EnumLocalizeValue` attribute.

```
[EnumLocalize] // Enable localization support
public enum DemoEnum
{
    [EnumLocalizeKey("localize_key_1")] // Specific localization key
    Value1,

    [EnumLocalizeValue("localize_value_2")] // Specific localization value
    Value2,

    [EnumLocalizeKey("localize_key_3")] // If key and value both exist, will prefer localization key
    [EnumLocalizeValue("localize_value_3")]
    Value3,

    [EnumLocalizeKey(nameof(Localize.flowlauncher_plugin_localization_demo_plugin_description))] // Use Localize class
    Value4,
}
```

Then you can get `DemoEnumData` class.

In view model class which needs to display it on a combo box control, you can add two fields for binding `ItemSource` and `SelectedValue` like:

```
public List<DemoEnumData> AllDemoEnums { get; } = DemoEnumData.GetValues(); // ItemSource of ComboBox

public DemoEnum SelectedDemoEnum { get; set; } // SelectedValue of ComboBox
```

```
<ComboBox
    DisplayMemberPath="Display"
    ItemsSource="{Binding AllDemoEnums}"
    SelectedValue="{Binding SelectedDemoEnum}"
    SelectedValuePath="Value" />
```

If you want to update localization strings when culture info changes, you can call this function to update.

```
private void UpdateEnumDropdownLocalizations()
{
    DemoEnumData.UpdateLabels(AllDemoEnums);
}
```
