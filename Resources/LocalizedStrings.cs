using System;
using System.ComponentModel;
using System.Globalization;

namespace LlamaServerLauncher.Resources;

public class LocalizedStrings : INotifyPropertyChanged
{
    private static readonly System.Resources.ResourceManager _resourceManager = new("LlamaServerLauncher.Resources.Strings", typeof(LocalizedStrings).Assembly);
    private static CultureInfo _currentCulture = CultureInfo.InvariantCulture;
    
    public static event Action? CultureChanged;
    
    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key] => GetString(key);

    public static string GetString(string key)
    {
        return _resourceManager.GetString(key, _currentCulture) ?? key;
    }

    public static void SetCulture(CultureInfo culture)
    {
        _currentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        Instance.OnPropertyChanged(string.Empty);
        CultureChanged?.Invoke();
    }

    public static CultureInfo CurrentCulture => _currentCulture;

    public static LocalizedStrings Instance { get; } = new();

    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string WindowTitle => GetString("WindowTitle");
    public string ServerControl => GetString("ServerControl");
    public string StartServer => GetString("StartServer");
    public string StopServer => GetString("StopServer");
    public string UnloadModel => GetString("UnloadModel");
    public string Show => GetString("Show");
    public string Close => GetString("Close");
    public string AutoRestartOnCrash => GetString("AutoRestartOnCrash");
    public string StatusStopped => GetString("StatusStopped");
    public string TabProfiles => GetString("TabProfiles");
    public string TabMain => GetString("TabMain");
    public string TabGeneration => GetString("TabGeneration");
    public string TabOptions => GetString("TabOptions");
    public string Profile => GetString("Profile");
    public string Save => GetString("Save");
    public string Load => GetString("Load");
    public string Delete => GetString("Delete");
    public string Rename => GetString("Rename");
    public string Clear => GetString("Clear");
    public string Export => GetString("Export");
    public string ExportDialogTitle => GetString("ExportDialogTitle");
    public string ExportFormatJson => GetString("ExportFormatJson");
    public string ExportFormatBat => GetString("ExportFormatBat");
    public string ExportToBat => GetString("ExportToBat");
    public string Import => GetString("Import");
    public string Paths => GetString("Paths");
    public string LlamaServerExe => GetString("LlamaServerExe");
    public string ModelM => GetString("ModelM");
    public string ModelsDir => GetString("ModelsDir");
    public string Browse => GetString("Browse");
    public string NetworkSettings => GetString("NetworkSettings");
    public string Host => GetString("Host");
    public string Port => GetString("Port");
    public string ModelParameters => GetString("ModelParameters");
    public string ContextSize => GetString("ContextSize");
    public string Threads => GetString("Threads");
    public string GpuLayers => GetString("GpuLayers");
    public string GenerationParameters => GetString("GenerationParameters");
    public string Temperature => GetString("Temperature");
    public string MaxTokens => GetString("MaxTokens");
    public string BatchSize => GetString("BatchSize");
    public string UBatchSize => GetString("UBatchSize");
    public string MinP => GetString("MinP");
    public string MMProj => GetString("MMProj");
    public string CacheTypeK => GetString("CacheTypeK");
    public string CacheTypeV => GetString("CacheTypeV");
    public string TopK => GetString("TopK");
    public string TopP => GetString("TopP");
    public string RepeatPenalty => GetString("RepeatPenalty");
    public string AdditionalOptions => GetString("AdditionalOptions");
    public string FlashAttention => GetString("FlashAttention");
    public string WebUI => GetString("WebUI");
    public string Embedding => GetString("Embedding");
    public string Slots => GetString("Slots");
    public string Metrics => GetString("Metrics");
    public string ApiKey => GetString("ApiKey");
    public string Alias => GetString("Alias");
    public string LogFile => GetString("LogFile");
    public string VerboseLogging => GetString("VerboseLogging");
    public string CustomArguments => GetString("CustomArguments");
    public string ServerLog => GetString("ServerLog");
    public string ClearLog => GetString("ClearLog");
    public string AutoScrollLog => GetString("AutoScrollLog");
    public string Language => GetString("Language");
public string English => GetString("English");
    public string Russian => GetString("Russian");

    public string ErrorTitle => GetString("ErrorTitle");
    public string WarningTitle => GetString("WarningTitle");
    public string ConfirmTitle => GetString("ConfirmTitle");
    public string OK => GetString("OK");
    public string Cancel => GetString("Cancel");
    public string Yes => GetString("Yes");
    public string No => GetString("No");
    public string FailedToStartServer => GetString("FailedToStartServer");
    public string PleaseEnterProfileName => GetString("PleaseEnterProfileName");
    public string UnsavedChangesTitle => GetString("UnsavedChangesTitle");
    public string UnsavedChangesMessage => GetString("UnsavedChangesMessage");
    public string UnsavedChangesWarning => GetString("UnsavedChangesWarning");
    public string AutoRestarting => GetString("AutoRestarting");
    public string FailedToAutoRestart => GetString("FailedToAutoRestart");
    public string ConfirmCloseTitle => GetString("ConfirmCloseTitle");
    public string ConfirmCloseMessage => GetString("ConfirmCloseMessage");
    public string DropBatFile => GetString("DropBatFile");
    public string TooltipClearAllFields => GetString("TooltipClearAllFields");
    public string EnterNewProfileName => GetString("EnterNewProfileName");
    public string RenameProfileFailed => GetString("RenameProfileFailed");
    public string ConfirmDelete => GetString("ConfirmDelete");
    public string ErrorLoadingFile => GetString("ErrorLoadingFile");
    public string ExportAll => GetString("ExportAll");
    public string ImportAll => GetString("ImportAll");
    public string ExportAllDialogTitle => GetString("ExportAllDialogTitle");
    public string ImportAllDialogTitle => GetString("ImportAllDialogTitle");
    public string ExportAllSuccess => GetString("ExportAllSuccess");
    public string ImportAllSuccess => GetString("ImportAllSuccess");
    public string ExportAllFailed => GetString("ExportAllFailed");
    public string ImportAllFailed => GetString("ImportAllFailed");
    public string ExportAllFilter => GetString("ExportAllFilter");
    public string ImportAllFilter => GetString("ImportAllFilter");
    public string NoProfilesToExport => GetString("NoProfilesToExport");
    public string TooltipExportAll => GetString("TooltipExportAll");
    public string TooltipImportAll => GetString("TooltipImportAll");

    public string TooltipModelPath => GetString("TooltipModelPath");
    public string TooltipModelsDir => GetString("TooltipModelsDir");
    public string TooltipHost => GetString("TooltipHost");
    public string TooltipPort => GetString("TooltipPort");
    public string TooltipContextSize => GetString("TooltipContextSize");
    public string TooltipThreads => GetString("TooltipThreads");
    public string TooltipGpuLayers => GetString("TooltipGpuLayers");
    public string TooltipTemperature => GetString("TooltipTemperature");
    public string TooltipMaxTokens => GetString("TooltipMaxTokens");
    public string TooltipBatchSize => GetString("TooltipBatchSize");
    public string TooltipUBatchSize => GetString("TooltipUBatchSize");
    public string TooltipMinP => GetString("TooltipMinP");
    public string TooltipMMProj => GetString("TooltipMMProj");
    public string TooltipCacheTypeK => GetString("TooltipCacheTypeK");
    public string TooltipCacheTypeV => GetString("TooltipCacheTypeV");
    public string TooltipTopK => GetString("TooltipTopK");
    public string TooltipTopP => GetString("TooltipTopP");
    public string TooltipRepeatPenalty => GetString("TooltipRepeatPenalty");
    public string TooltipFlashAttention => GetString("TooltipFlashAttention");
    public string TooltipWebUI => GetString("TooltipWebUI");
    public string TooltipEmbedding => GetString("TooltipEmbedding");
    public string TooltipSlots => GetString("TooltipSlots");
    public string TooltipMetrics => GetString("TooltipMetrics");
    public string TooltipApiKey => GetString("TooltipApiKey");
    public string TooltipAlias => GetString("TooltipAlias");
    public string TooltipLogFile => GetString("TooltipLogFile");
    public string TooltipCustomArguments => GetString("TooltipCustomArguments");
}