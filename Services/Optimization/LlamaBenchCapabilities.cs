using System.Linq;
using System.Threading.Tasks;

namespace LlamaServerLauncher.Services.Optimization;

public sealed class LlamaBenchCapabilities
{
    public bool SupportsNoWarmup { get; init; }

    public bool SupportsOverrideTensor { get; init; } = true;

    public bool SupportsCtxSize { get; init; }

    public string CtxFlag { get; init; } = "-c";

    public bool SupportsCacheType { get; init; }

    public bool SupportsMmproj { get; init; }

    public bool SupportsNCpuMoe { get; init; }

    public bool SupportsFit { get; init; }

    public string FitTargetFlag { get; init; } = "--fit-target";

    public string FitCtxFlag { get; init; } = "--fit-ctx";

    public FlashAttnStyle FlashAttn { get; init; } = FlashAttnStyle.Integer;

    public bool FlashAttnAssumedDefault { get; init; }

    public static LlamaBenchCapabilities Default => new()
    {
        SupportsNoWarmup = false,
        SupportsOverrideTensor = true,
        SupportsCtxSize = false,
        SupportsCacheType = false,
        SupportsMmproj = false,
        FlashAttn = FlashAttnStyle.Integer,
        FlashAttnAssumedDefault = true,
    };

    public static async Task<LlamaBenchCapabilities> DetectAsync(string benchExePath)
    {
        var help = await LlamaHelpParserService.GetSupportedFlagsWithHelpAsync(benchExePath);
        if (help == null)
            return Default;

        var flags = help.Flags;
        bool noWarmup = flags.Contains("--no-warmup");
        bool overrideTensor = flags.Contains("--override-tensor") || flags.Contains("-ot");

        string? ctxFlag = new[] { "--ctx-size", "--n-ctx" }.FirstOrDefault(flags.Contains);
        bool cacheType = (flags.Contains("-ctk") || flags.Contains("--cache-type-k"))
                         && (flags.Contains("-ctv") || flags.Contains("--cache-type-v"));
        bool mmproj = flags.Contains("--mmproj");
        bool nCpuMoe = flags.Contains("--n-cpu-moe") || flags.Contains("-ncmoe");

        bool fit = (flags.Contains("--fit-target") || flags.Contains("-fitt"))
                   && (flags.Contains("--fit-ctx") || flags.Contains("-fitc"));

        var faStyle = FlashAttnStyle.Integer;
        bool assumed = true;
        if (flags.Contains("--flash-attn") || flags.Contains("-fa"))
        {
            var values = LlamaHelpParserService.ParseFlagValues(help.HelpText, "--flash-attn");
            if (values != null && values.Count > 0)
            {
                bool hasOnOff = values.Any(v =>
                    v.Equals("on", System.StringComparison.OrdinalIgnoreCase) ||
                    v.Equals("off", System.StringComparison.OrdinalIgnoreCase));
                faStyle = hasOnOff ? FlashAttnStyle.OnOff : FlashAttnStyle.Integer;
                assumed = false;
            }
        }

        return new LlamaBenchCapabilities
        {
            SupportsNoWarmup = noWarmup,
            SupportsOverrideTensor = overrideTensor,
            SupportsCtxSize = ctxFlag != null,
            CtxFlag = ctxFlag ?? "-c",
            SupportsCacheType = cacheType,
            SupportsMmproj = mmproj,
            SupportsNCpuMoe = nCpuMoe,
            SupportsFit = fit,
            FlashAttn = faStyle,
            FlashAttnAssumedDefault = assumed,
        };
    }
}
