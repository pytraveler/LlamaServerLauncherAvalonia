using System;
using System.Collections.Generic;
using System.Globalization;
using LlamaServerLauncher.Resources;

namespace LlamaServerLauncher.Models;

public static class LlamaArgumentRegistry
{
    private static readonly Dictionary<string, LlamaArgumentDefinition> _byFlag = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<LlamaArgumentDefinition> _all = new();

    static LlamaArgumentRegistry()
    {
        var defs = new List<LlamaArgumentDefinition>
        {
            // ===== common params =====

            new()
            {
                PrimaryFlag = "-t",
                Aliases = new() { "-t", "--threads" },
                DescriptionEn = "Number of CPU threads to use during generation",
                DescriptionRu = "Количество потоков CPU для генерации",
                DefaultValue = "-1",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-tb",
                Aliases = new() { "-tb", "--threads-batch" },
                DescriptionEn = "Number of threads to use during batch and prompt processing",
                DescriptionRu = "Количество потоков для обработки пакета и промпта",
                DefaultValue = "same as --threads",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-C",
                Aliases = new() { "-C", "--cpu-mask" },
                DescriptionEn = "CPU affinity mask: arbitrarily long hex. Complements cpu-range",
                DescriptionRu = "Маска привязки CPU: произвольная длинная шестнадцатеричная строка. Дополняет cpu-range",
                DefaultValue = "",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-Cr",
                Aliases = new() { "-Cr", "--cpu-range" },
                DescriptionEn = "Range of CPUs for affinity. Complements --cpu-mask",
                DescriptionRu = "Диапазон CPU для привязки. Дополняет --cpu-mask",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--cpu-strict",
                Aliases = new() { "--cpu-strict" },
                DescriptionEn = "Use strict CPU placement",
                DescriptionRu = "Использовать строгое размещение CPU",
                DefaultValue = "0",
                AllowedValues = new() { "0", "1" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--prio",
                Aliases = new() { "--prio" },
                DescriptionEn = "Set process/thread priority: low(-1), normal(0), medium(1), high(2), realtime(3)",
                DescriptionRu = "Приоритет процесса/потока: low(-1), normal(0), medium(1), high(2), realtime(3)",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--poll",
                Aliases = new() { "--poll" },
                DescriptionEn = "Use polling level to wait for work (0 = no polling)",
                DescriptionRu = "Уровень опроса для ожидания работы (0 = без опроса)",
                DefaultValue = "50",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-Cb",
                Aliases = new() { "-Cb", "--cpu-mask-batch" },
                DescriptionEn = "CPU affinity mask for batch processing. Complements cpu-range-batch",
                DescriptionRu = "Маска привязки CPU для пакетной обработки. Дополняет cpu-range-batch",
                DefaultValue = "same as --cpu-mask",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-Crb",
                Aliases = new() { "-Crb", "--cpu-range-batch" },
                DescriptionEn = "Ranges of CPUs for affinity during batch processing. Complements --cpu-mask-batch",
                DescriptionRu = "Диапазоны CPU для привязки при пакетной обработке. Дополняет --cpu-mask-batch",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--cpu-strict-batch",
                Aliases = new() { "--cpu-strict-batch" },
                DescriptionEn = "Use strict CPU placement for batch processing",
                DescriptionRu = "Строгое размещение CPU для пакетной обработки",
                DefaultValue = "same as --cpu-strict",
                AllowedValues = new() { "0", "1" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--prio-batch",
                Aliases = new() { "--prio-batch" },
                DescriptionEn = "Set process/thread priority for batch: 0-normal, 1-medium, 2-high, 3-realtime",
                DescriptionRu = "Приоритет процесса/потока для пакета: 0-нормальный, 1-средний, 2-высокий, 3-реального времени",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--poll-batch",
                Aliases = new() { "--poll-batch" },
                DescriptionEn = "Use polling to wait for batch work",
                DescriptionRu = "Использовать опрос для ожидания пакетной работы",
                DefaultValue = "same as --poll",
                AllowedValues = new() { "0", "1" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-c",
                Aliases = new() { "-c", "--ctx-size" },
                DescriptionEn = "Size of the prompt context (0 = loaded from model)",
                DescriptionRu = "Размер контекста промпта (0 = загружается из модели)",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-n",
                Aliases = new() { "-n", "--predict", "--n-predict" },
                DescriptionEn = "Number of tokens to predict (-1 = infinity)",
                DescriptionRu = "Количество токенов для предсказания (-1 = бесконечно)",
                DefaultValue = "-1",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-b",
                Aliases = new() { "-b", "--batch-size" },
                DescriptionEn = "Logical maximum batch size",
                DescriptionRu = "Логический максимальный размер пакета",
                DefaultValue = "2048",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ub",
                Aliases = new() { "-ub", "--ubatch-size" },
                DescriptionEn = "Physical maximum batch size",
                DescriptionRu = "Физический максимальный размер пакета",
                DefaultValue = "512",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--keep",
                Aliases = new() { "--keep" },
                DescriptionEn = "Number of tokens to keep from the initial prompt (0, -1 = all)",
                DescriptionRu = "Количество токенов для сохранения из начального промпта (0, -1 = все)",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--swa-full",
                Aliases = new() { "--swa-full" },
                DescriptionEn = "Use full-size SWA cache",
                DescriptionRu = "Использовать кэш SWA полного размера",
                DefaultValue = "false",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-fa",
                Aliases = new() { "-fa", "--flash-attn" },
                DescriptionEn = "Set Flash Attention use ('on', 'off', or 'auto')",
                DescriptionRu = "Использование Flash Attention ('on', 'off' или 'auto')",
                DefaultValue = "auto",
                AllowedValues = new() { "on", "off", "auto" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--perf",
                Aliases = new() { "--perf", "--no-perf" },
                DescriptionEn = "Whether to enable internal libllama performance timings",
                DescriptionRu = "Включить внутренние замеры производительности libllama",
                DefaultValue = "false",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-e",
                Aliases = new() { "-e", "--escape", "--no-escape" },
                DescriptionEn = "Whether to process escape sequences (\\n, \\r, \\t, \\', \\\", \\\\)",
                DescriptionRu = "Обрабатывать escape-последовательности (\\n, \\r, \\t, \\', \\\", \\\\)",
                DefaultValue = "true",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--rope-scaling",
                Aliases = new() { "--rope-scaling" },
                DescriptionEn = "RoPE frequency scaling method, defaults to linear unless specified by the model",
                DescriptionRu = "Метод масштабирования частоты RoPE, по умолчанию linear если не указано моделью",
                AllowedValues = new() { "none", "linear", "yarn" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--rope-scale",
                Aliases = new() { "--rope-scale" },
                DescriptionEn = "RoPE context scaling factor, expands context by a factor of N",
                DescriptionRu = "Коэффициент масштабирования контекста RoPE, расширяет контекст в N раз",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--rope-freq-base",
                Aliases = new() { "--rope-freq-base" },
                DescriptionEn = "RoPE base frequency, used by NTK-aware scaling",
                DescriptionRu = "Базовая частота RoPE, используется при NTK-масштабировании",
                DefaultValue = "loaded from model",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--rope-freq-scale",
                Aliases = new() { "--rope-freq-scale" },
                DescriptionEn = "RoPE frequency scaling factor, expands context by a factor of 1/N",
                DescriptionRu = "Коэффициент масштабирования частоты RoPE, расширяет контекст в 1/N раз",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--yarn-orig-ctx",
                Aliases = new() { "--yarn-orig-ctx" },
                DescriptionEn = "YaRN: original context size of model (0 = model training context size)",
                DescriptionRu = "YaRN: исходный размер контекста модели (0 = размер контекста обучения)",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--yarn-ext-factor",
                Aliases = new() { "--yarn-ext-factor" },
                DescriptionEn = "YaRN: extrapolation mix factor (0.0 = full interpolation)",
                DescriptionRu = "YaRN: коэффициент смешивания экстраполяции (0.0 = полная интерполяция)",
                DefaultValue = "-1.00",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--yarn-attn-factor",
                Aliases = new() { "--yarn-attn-factor" },
                DescriptionEn = "YaRN: scale sqrt(t) or attention magnitude",
                DescriptionRu = "YaRN: масштаб sqrt(t) или величина внимания",
                DefaultValue = "-1.00",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--yarn-beta-slow",
                Aliases = new() { "--yarn-beta-slow" },
                DescriptionEn = "YaRN: high correction dim or alpha",
                DescriptionRu = "YaRN: размерность верхней коррекции или alpha",
                DefaultValue = "-1.00",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--yarn-beta-fast",
                Aliases = new() { "--yarn-beta-fast" },
                DescriptionEn = "YaRN: low correction dim or beta",
                DescriptionRu = "YaRN: размерность нижней коррекции или beta",
                DefaultValue = "-1.00",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-kvo",
                Aliases = new() { "-kvo", "--kv-offload", "-nkvo", "--no-kv-offload" },
                DescriptionEn = "Whether to enable KV cache offloading",
                DescriptionRu = "Включить выгрузку кэша KV",
                DefaultValue = "enabled",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--repack",
                Aliases = new() { "--repack", "-nr", "--no-repack" },
                DescriptionEn = "Whether to enable weight repacking",
                DescriptionRu = "Включить перепаковку весов",
                DefaultValue = "enabled",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--no-host",
                Aliases = new() { "--no-host" },
                DescriptionEn = "Bypass host buffer allowing extra buffers to be used",
                DescriptionRu = "Обойти host-буфер, разрешив использование дополнительных буферов",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ctk",
                Aliases = new() { "-ctk", "--cache-type-k" },
                DescriptionEn = "KV cache data type for K",
                DescriptionRu = "Тип данных кэша KV для K",
                DefaultValue = "f16",
                AllowedValues = new() { "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ctv",
                Aliases = new() { "-ctv", "--cache-type-v" },
                DescriptionEn = "KV cache data type for V",
                DescriptionRu = "Тип данных кэша KV для V",
                DefaultValue = "f16",
                AllowedValues = new() { "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-dt",
                Aliases = new() { "-dt", "--defrag-thold" },
                DescriptionEn = "KV cache defragmentation threshold (DEPRECATED)",
                DescriptionRu = "Порог дефрагментации кэша KV (УСТАРЕЛО)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--rpc",
                Aliases = new() { "--rpc" },
                DescriptionEn = "Comma-separated list of RPC servers (host:port)",
                DescriptionRu = "Список RPC-серверов через запятую (host:port)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--mlock",
                Aliases = new() { "--mlock" },
                DescriptionEn = "Force system to keep model in RAM rather than swapping or compressing",
                DescriptionRu = "Принудительно удерживать модель в ОЗУ вместо свопинга или сжатия",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--mmap",
                Aliases = new() { "--mmap", "--no-mmap" },
                DescriptionEn = "Whether to memory-map model (if disabled, slower load but may reduce pageouts)",
                DescriptionRu = "Использовать mmap для модели (если отключено, медленная загрузка, но может уменьшить pageouts)",
                DefaultValue = "enabled",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-dio",
                Aliases = new() { "-dio", "--direct-io", "-ndio", "--no-direct-io" },
                DescriptionEn = "Use DirectIO if available",
                DescriptionRu = "Использовать DirectIO, если доступно",
                DefaultValue = "disabled",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-lm",
                Aliases = new() { "-lm", "--load-mode" },
                DescriptionEn = "Model loading mode: auto (mmap unless a device does not support it), none, mmap, mlock (keep the model in RAM), mmap+mlock or dio (DirectIO if available). When set here, the mmap and mlock switches are not passed",
                DescriptionRu = "Режим загрузки модели: auto (mmap, если устройство его поддерживает), none, mmap, mlock (держать модель в RAM без выгрузки), mmap+mlock или dio (DirectIO, если доступен). Если задан здесь, переключатели mmap и mlock не передаются",
                DefaultValue = "auto",
                AllowedValues = new() { "auto", "none", "mmap", "mlock", "mmap+mlock", "dio" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-lzm",
                Aliases = new() { "-lzm", "--lazy-mode" },
                DescriptionEn = "On-demand reading of some large tensors, such as per-layer embeddings: on reads their rows from disk when needed (requires mmap), auto does so only for tensors over 4 GiB, off keeps them resident",
                DescriptionRu = "Чтение некоторых крупных тензоров с диска по требованию, например per-layer embeddings: on читает их строки при обращении (нужен mmap), auto делает так только для тензоров больше 4 ГиБ, off держит их в памяти",
                DefaultValue = "auto",
                AllowedValues = new() { "on", "auto", "off" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--numa",
                Aliases = new() { "--numa" },
                DescriptionEn = "Attempt optimizations that help on some NUMA systems (distribute, isolate, numactl)",
                DescriptionRu = "Оптимизации для NUMA-систем (distribute, isolate, numactl)",
                AllowedValues = new() { "distribute", "isolate", "numactl" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-dev",
                Aliases = new() { "-dev", "--device" },
                DescriptionEn = "Comma-separated list of devices to use for offloading (none = don't offload)",
                DescriptionRu = "Список устройств для офлоада через запятую (none = не офлоадить)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--list-devices",
                Aliases = new() { "--list-devices" },
                DescriptionEn = "Print list of available devices and exit",
                DescriptionRu = "Показать список доступных устройств и выйти",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ot",
                Aliases = new() { "-ot", "--override-tensor" },
                DescriptionEn = "Override tensor buffer type (pattern=buffer_type,...)",
                DescriptionRu = "Переопределить тип буфера тензоров (шаблон=тип_буфера,...)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-cmoe",
                Aliases = new() { "-cmoe", "--cpu-moe" },
                DescriptionEn = "Keep all Mixture of Experts (MoE) weights in the CPU",
                DescriptionRu = "Хранить все веса MoE (Mixture of Experts) в CPU",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ncmoe",
                Aliases = new() { "-ncmoe", "--n-cpu-moe" },
                DescriptionEn = "Keep the MoE weights of the first N layers in the CPU",
                DescriptionRu = "Хранить веса MoE первых N слоёв в CPU",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--moe-cache-mib",
                Aliases = new() { "--moe-cache-mib" },
                DescriptionEn = "GPU cache size in MiB for the MoE experts kept in the CPU; with multiple GPUs it is split like the layers (--tensor-split)",
                DescriptionRu = "Размер кэша на GPU в МиБ для экспертов MoE, оставленных на CPU; на нескольких GPU делится как слои (--tensor-split)",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ncffn",
                Aliases = new() { "-ncffn", "--n-cpu-ffn" },
                DescriptionEn = "Keep the dense FFN weights of the first N layers in the CPU (dense models; for MoE experts use --n-cpu-moe)",
                DescriptionRu = "Хранить веса плотного FFN первых N слоёв в CPU (плотные модели; для экспертов MoE - --n-cpu-moe)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ngl",
                Aliases = new() { "-ngl", "--gpu-layers", "--n-gpu-layers" },
                DescriptionEn = "Max number of layers to store in VRAM ('auto', 'all', or exact number)",
                DescriptionRu = "Макс. количество слоёв для хранения в VRAM ('auto', 'all' или точное число)",
                DefaultValue = "auto",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-sm",
                Aliases = new() { "-sm", "--split-mode" },
                DescriptionEn = "How to split the model across multiple GPUs",
                DescriptionRu = "Как разделить модель между несколькими GPU",
                DefaultValue = "layer",
                AllowedValues = new() { "none", "layer", "row", "tensor" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ts",
                Aliases = new() { "-ts", "--tensor-split" },
                DescriptionEn = "Fraction of the model to offload to each GPU, comma-separated proportions",
                DescriptionRu = "Доля модели для офлоада на каждый GPU, пропорции через запятую",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-mg",
                Aliases = new() { "-mg", "--main-gpu" },
                DescriptionEn = "The GPU to use for the model or for intermediate results and KV",
                DescriptionRu = "GPU для модели или для промежуточных результатов и KV",
                DefaultValue = "0",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-fit",
                Aliases = new() { "-fit", "--fit" },
                DescriptionEn = "Whether to adjust unset arguments to fit in device memory",
                DescriptionRu = "Корректировать ли неустановленные аргументы для помещения в память устройства",
                DefaultValue = "on",
                AllowedValues = new() { "on", "off" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-fitt",
                Aliases = new() { "-fitt", "--fit-target" },
                DescriptionEn = "Target margin per device for --fit, comma-separated MiB values",
                DescriptionRu = "Целевой запас памяти на устройство для --fit, значения в МиБ через запятую",
                DefaultValue = "1024",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-fitc",
                Aliases = new() { "-fitc", "--fit-ctx" },
                DescriptionEn = "Minimum ctx size that can be set by --fit option",
                DescriptionRu = "Минимальный размер контекста, который может быть установлен через --fit",
                DefaultValue = "4096",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--check-tensors",
                Aliases = new() { "--check-tensors" },
                DescriptionEn = "Check model tensor data for invalid values",
                DescriptionRu = "Проверить данные тензоров модели на недопустимые значения",
                DefaultValue = "false",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--override-kv",
                Aliases = new() { "--override-kv" },
                DescriptionEn = "Override model metadata by key (KEY=TYPE:VALUE,...)",
                DescriptionRu = "Переопределить метаданные модели по ключу (KEY=TYPE:VALUE,...)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--op-offload",
                Aliases = new() { "--op-offload", "--no-op-offload" },
                DescriptionEn = "Whether to offload host tensor operations to device",
                DescriptionRu = "Офлоадить тензорные операции хоста на устройство",
                DefaultValue = "true",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--lora",
                Aliases = new() { "--lora" },
                DescriptionEn = "Path to LoRA adapter (comma-separated for multiple)",
                DescriptionRu = "Путь к адаптеру LoRA (через запятую для нескольких)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--lora-scaled",
                Aliases = new() { "--lora-scaled" },
                DescriptionEn = "Path to LoRA adapter with user defined scaling (FNAME:SCALE,...)",
                DescriptionRu = "Путь к адаптеру LoRA с пользовательским масштабированием (FNAME:SCALE,...)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--control-vector",
                Aliases = new() { "--control-vector" },
                DescriptionEn = "Add a control vector (comma-separated for multiple)",
                DescriptionRu = "Добавить вектор управления (через запятую для нескольких)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--control-vector-scaled",
                Aliases = new() { "--control-vector-scaled" },
                DescriptionEn = "Add a control vector with user defined scaling (FNAME:SCALE,...)",
                DescriptionRu = "Добавить вектор управления с масштабированием (FNAME:SCALE,...)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--control-vector-layer-range",
                Aliases = new() { "--control-vector-layer-range" },
                DescriptionEn = "Layer range to apply the control vector(s) to (start and end inclusive)",
                DescriptionRu = "Диапазон слоёв для применения вектора(ов) управления (начало и конец включительно)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-m",
                Aliases = new() { "-m", "--model" },
                DescriptionEn = "Model path to load",
                DescriptionRu = "Путь к модели для загрузки",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-mu",
                Aliases = new() { "-mu", "--model-url" },
                DescriptionEn = "Model download URL",
                DescriptionRu = "URL для скачивания модели",
                DefaultValue = "unused",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-dr",
                Aliases = new() { "-dr", "--docker-repo" },
                DescriptionEn = "Docker Hub model repository ([repo/]model[:quant])",
                DescriptionRu = "Репозиторий моделей Docker Hub ([repo/]model[:quant])",
                DefaultValue = "unused",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-hf",
                Aliases = new() { "-hf", "-hfr", "--hf-repo" },
                DescriptionEn = "Hugging Face model repository (<user>/<model>[:quant])",
                DescriptionRu = "Репозиторий моделей Hugging Face (<user>/<model>[:quant])",
                DefaultValue = "unused",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-hff",
                Aliases = new() { "-hff", "--hf-file" },
                DescriptionEn = "Hugging Face model file. Overrides the quant in --hf-repo",
                DescriptionRu = "Файл модели Hugging Face. Переопределяет квантование в --hf-repo",
                DefaultValue = "unused",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-hft",
                Aliases = new() { "-hft", "--hf-token" },
                DescriptionEn = "Hugging Face access token",
                DescriptionRu = "Токен доступа Hugging Face",
                DefaultValue = "value from HF_TOKEN env",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--log-disable",
                Aliases = new() { "--log-disable" },
                DescriptionEn = "Disable logging",
                DescriptionRu = "Отключить логирование",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--log-file",
                Aliases = new() { "--log-file" },
                DescriptionEn = "Log to file",
                DescriptionRu = "Записывать лог в файл",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--log-colors",
                Aliases = new() { "--log-colors" },
                DescriptionEn = "Set colored logging ('on', 'off', or 'auto')",
                DescriptionRu = "Цветной лог ('on', 'off' или 'auto')",
                DefaultValue = "auto",
                AllowedValues = new() { "on", "off", "auto" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--log-jsonl",
                Aliases = new() { "--log-jsonl", "--no-log-jsonl" },
                DescriptionEn = "Log as JSONL (one JSON object per line) to stdout; also turns off colored logging. The launcher reads the plain text log, so its speed and memory readouts may stop working",
                DescriptionRu = "Писать лог в stdout в формате JSONL (один JSON-объект на строку); заодно отключает цветной вывод. Лаунчер читает обычный текстовый лог, поэтому показатели скорости и памяти могут перестать работать",
                DefaultValue = "disabled",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-v",
                Aliases = new() { "-v", "--verbose", "--log-verbose" },
                DescriptionEn = "Set verbosity level to infinity (log all messages, useful for debugging)",
                DescriptionRu = "Установить уровень детализации на бесконечность (логировать все сообщения)",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--offline",
                Aliases = new() { "--offline" },
                DescriptionEn = "Offline mode: forces use of cache, prevents network access",
                DescriptionRu = "Автономный режим: принудительно использует кэш, блокирует сетевой доступ",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-lv",
                Aliases = new() { "-lv", "--verbosity", "--log-verbosity" },
                DescriptionEn = "Set the verbosity threshold (0=generic, 1=error, 2=warning, 3=info, 4=trace, 5=debug)",
                DescriptionRu = "Порог детализации (0=общий, 1=ошибка, 2=предупреждение, 3=инфо, 4=trace, 5=debug)",
                DefaultValue = "3",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--log-prefix",
                Aliases = new() { "--log-prefix", "--no-log-prefix" },
                DescriptionEn = "Enable prefix in log messages",
                DescriptionRu = "Включить префикс в сообщениях лога",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "--log-timestamps",
                Aliases = new() { "--log-timestamps", "--no-log-timestamps" },
                DescriptionEn = "Enable timestamps in log messages",
                DescriptionRu = "Включить метки времени в сообщениях лога",
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ctkd",
                Aliases = new() { "--spec-draft-type-k", "-ctkd", "--cache-type-k-draft" },
                DescriptionEn = "KV cache data type for K for the draft model",
                DescriptionRu = "Тип данных кэша KV для K черновой модели",
                DefaultValue = "f16",
                AllowedValues = new() { "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" },
                Category = "common"
            },
            new()
            {
                PrimaryFlag = "-ctvd",
                Aliases = new() { "--spec-draft-type-v", "-ctvd", "--cache-type-v-draft" },
                DescriptionEn = "KV cache data type for V for the draft model",
                DescriptionRu = "Тип данных кэша KV для V черновой модели",
                DefaultValue = "f16",
                AllowedValues = new() { "f32", "f16", "bf16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1" },
                Category = "common"
            },

            // ===== sampling params =====

            new()
            {
                PrimaryFlag = "--samplers",
                Aliases = new() { "--samplers" },
                DescriptionEn = "Samplers for generation in the order, separated by ';'",
                DescriptionRu = "Сэмплеры для генерации по порядку, разделённые ';'",
                DefaultValue = "penalties;dry;top_n_sigma;top_k;typ_p;top_p;min_p;xtc;temperature",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "-s",
                Aliases = new() { "-s", "--seed" },
                DescriptionEn = "RNG seed (-1 = use random seed)",
                DescriptionRu = "Начальное значение ГПСЧ (-1 = случайное)",
                DefaultValue = "-1",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--sampler-seq",
                Aliases = new() { "--sampler-seq", "--sampling-seq" },
                DescriptionEn = "Simplified sequence for samplers",
                DescriptionRu = "Упрощённая последовательность сэмплеров",
                DefaultValue = "edskypmxt",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--ignore-eos",
                Aliases = new() { "--ignore-eos" },
                DescriptionEn = "Ignore end of stream token and continue generating",
                DescriptionRu = "Игнорировать токен конца потока и продолжить генерацию",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--temp",
                Aliases = new() { "--temp", "--temperature" },
                DescriptionEn = "Temperature",
                DescriptionRu = "Температура",
                DefaultValue = "0.80",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--top-k",
                Aliases = new() { "--top-k" },
                DescriptionEn = "Top-k sampling (0 = disabled)",
                DescriptionRu = "Сэмплирование top-k (0 = отключено)",
                DefaultValue = "40",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--top-p",
                Aliases = new() { "--top-p" },
                DescriptionEn = "Top-p sampling (1.0 = disabled)",
                DescriptionRu = "Сэмплирование top-p (1.0 = отключено)",
                DefaultValue = "0.95",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--min-p",
                Aliases = new() { "--min-p" },
                DescriptionEn = "Min-p sampling (0.0 = disabled)",
                DescriptionRu = "Сэмплирование min-p (0.0 = отключено)",
                DefaultValue = "0.05",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--top-nsigma",
                Aliases = new() { "--top-nsigma", "--top-n-sigma" },
                DescriptionEn = "Top-n-sigma sampling (-1.0 = disabled)",
                DescriptionRu = "Сэмплирование top-n-sigma (-1.0 = отключено)",
                DefaultValue = "-1.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--xtc-probability",
                Aliases = new() { "--xtc-probability" },
                DescriptionEn = "XTC probability (0.0 = disabled)",
                DescriptionRu = "Вероятность XTC (0.0 = отключено)",
                DefaultValue = "0.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--xtc-threshold",
                Aliases = new() { "--xtc-threshold" },
                DescriptionEn = "XTC threshold (1.0 = disabled)",
                DescriptionRu = "Порог XTC (1.0 = отключено)",
                DefaultValue = "0.10",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--typical",
                Aliases = new() { "--typical", "--typical-p" },
                DescriptionEn = "Locally typical sampling, parameter p (1.0 = disabled)",
                DescriptionRu = "Локально типичное сэмплирование, параметр p (1.0 = отключено)",
                DefaultValue = "1.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--repeat-last-n",
                Aliases = new() { "--repeat-last-n" },
                DescriptionEn = "Last n tokens to consider for penalize (0 = disabled)",
                DescriptionRu = "Последние n токенов для штрафования (0 = отключено)",
                DefaultValue = "64",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--repeat-penalty",
                Aliases = new() { "--repeat-penalty" },
                DescriptionEn = "Penalize repeat sequence of tokens (1.0 = disabled)",
                DescriptionRu = "Штраф за повторение последовательности токенов (1.0 = отключено)",
                DefaultValue = "1.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--presence-penalty",
                Aliases = new() { "--presence-penalty" },
                DescriptionEn = "Repeat alpha presence penalty (0.0 = disabled)",
                DescriptionRu = "Штраф за присутствие (alpha) (0.0 = отключено)",
                DefaultValue = "0.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--frequency-penalty",
                Aliases = new() { "--frequency-penalty" },
                DescriptionEn = "Repeat alpha frequency penalty (0.0 = disabled)",
                DescriptionRu = "Штраф за частоту (alpha) (0.0 = отключено)",
                DefaultValue = "0.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dry-multiplier",
                Aliases = new() { "--dry-multiplier" },
                DescriptionEn = "DRY sampling multiplier (0.0 = disabled)",
                DescriptionRu = "Множитель DRY-сэмплирования (0.0 = отключено)",
                DefaultValue = "0.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dry-base",
                Aliases = new() { "--dry-base" },
                DescriptionEn = "DRY sampling base value",
                DescriptionRu = "Базовое значение DRY-сэмплирования",
                DefaultValue = "1.75",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dry-allowed-length",
                Aliases = new() { "--dry-allowed-length" },
                DescriptionEn = "Allowed length for DRY sampling",
                DescriptionRu = "Допустимая длина для DRY-сэмплирования",
                DefaultValue = "2",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dry-penalty-last-n",
                Aliases = new() { "--dry-penalty-last-n" },
                DescriptionEn = "DRY penalty for the last n tokens (0 = disable)",
                DescriptionRu = "DRY-штраф за последние n токенов (0 = выкл)",
                DefaultValue = "64",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dry-sequence-breaker",
                Aliases = new() { "--dry-sequence-breaker" },
                DescriptionEn = "Add sequence breaker for DRY sampling, clearing out default breakers",
                DescriptionRu = "Добавить разделитель последовательностей для DRY-сэмплирования",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--adaptive-target",
                Aliases = new() { "--adaptive-target" },
                DescriptionEn = "Adaptive-p: select tokens near this probability (negative = disabled)",
                DescriptionRu = "Adaptive-p: выбирать токены около этой вероятности (отрицательное = отключено)",
                DefaultValue = "-1.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--adaptive-decay",
                Aliases = new() { "--adaptive-decay" },
                DescriptionEn = "Adaptive-p: decay rate for target adaptation (0.0 to 0.99)",
                DescriptionRu = "Adaptive-p: скорость затухания адаптации цели (от 0.0 до 0.99)",
                DefaultValue = "0.90",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dynatemp-range",
                Aliases = new() { "--dynatemp-range" },
                DescriptionEn = "Dynamic temperature range (0.0 = disabled)",
                DescriptionRu = "Диапазон динамической температуры (0.0 = отключено)",
                DefaultValue = "0.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--dynatemp-exp",
                Aliases = new() { "--dynatemp-exp" },
                DescriptionEn = "Dynamic temperature exponent",
                DescriptionRu = "Показатель динамической температуры",
                DefaultValue = "1.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--mirostat",
                Aliases = new() { "--mirostat" },
                DescriptionEn = "Use Mirostat sampling (0=disabled, 1=Mirostat, 2=Mirostat 2.0)",
                DescriptionRu = "Использовать сэмплирование Mirostat (0=выкл, 1=Mirostat, 2=Mirostat 2.0)",
                DefaultValue = "0",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--mirostat-lr",
                Aliases = new() { "--mirostat-lr" },
                DescriptionEn = "Mirostat learning rate, parameter eta",
                DescriptionRu = "Скорость обучения Mirostat, параметр eta",
                DefaultValue = "0.10",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--mirostat-ent",
                Aliases = new() { "--mirostat-ent" },
                DescriptionEn = "Mirostat target entropy, parameter tau",
                DescriptionRu = "Целевая энтропия Mirostat, параметр tau",
                DefaultValue = "5.00",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "-l",
                Aliases = new() { "-l", "--logit-bias" },
                DescriptionEn = "Modify the likelihood of token appearing in the completion",
                DescriptionRu = "Изменить вероятность появления токена в завершении",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--grammar",
                Aliases = new() { "--grammar" },
                DescriptionEn = "BNF-like grammar to constrain generations",
                DescriptionRu = "BNF-подобная грамматика для ограничения генерации",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "--grammar-file",
                Aliases = new() { "--grammar-file" },
                DescriptionEn = "File to read grammar from",
                DescriptionRu = "Файл для чтения грамматики",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "-j",
                Aliases = new() { "-j", "--json-schema" },
                DescriptionEn = "JSON schema to constrain generations",
                DescriptionRu = "JSON-схема для ограничения генерации",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "-jf",
                Aliases = new() { "-jf", "--json-schema-file" },
                DescriptionEn = "File containing a JSON schema to constrain generations",
                DescriptionRu = "Файл с JSON-схемой для ограничения генерации",
                Category = "sampling"
            },
            new()
            {
                PrimaryFlag = "-bs",
                Aliases = new() { "-bs", "--backend-sampling" },
                DescriptionEn = "Enable backend sampling (experimental)",
                DescriptionRu = "Включить серверное сэмплирование (экспериментальное)",
                DefaultValue = "disabled",
                Category = "sampling"
            },

            // ===== speculative params =====

            new()
            {
                PrimaryFlag = "-hfd",
                Aliases = new() { "--spec-draft-hf", "-hfd", "-hfrd", "--hf-repo-draft" },
                DescriptionEn = "Hugging Face model repository for the draft model",
                DescriptionRu = "Репозиторий моделей Hugging Face для черновой модели",
                DefaultValue = "unused",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-td",
                Aliases = new() { "--spec-draft-threads", "-td", "--threads-draft" },
                DescriptionEn = "Number of threads to use during generation for the draft model",
                DescriptionRu = "Количество потоков для генерации черновой модели",
                DefaultValue = "same as --threads",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-tbd",
                Aliases = new() { "--spec-draft-threads-batch", "-tbd", "--threads-batch-draft" },
                DescriptionEn = "Number of threads for batch/prompt processing for the draft model",
                DescriptionRu = "Количество потоков для пакетной обработки черновой модели",
                DefaultValue = "same as --threads-draft",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-Cd",
                Aliases = new() { "--spec-draft-cpu-mask", "-Cd", "--cpu-mask-draft" },
                DescriptionEn = "Draft model CPU affinity mask",
                DescriptionRu = "Маска привязки CPU для черновой модели",
                DefaultValue = "same as --cpu-mask",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-Crd",
                Aliases = new() { "--spec-draft-cpu-range", "-Crd", "--cpu-range-draft" },
                DescriptionEn = "Ranges of CPUs for affinity for the draft model",
                DescriptionRu = "Диапазоны CPU для привязки черновой модели",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-cpu-strict",
                Aliases = new() { "--spec-draft-cpu-strict", "--cpu-strict-draft" },
                DescriptionEn = "Use strict CPU placement for draft model",
                DescriptionRu = "Строгое размещение CPU для черновой модели",
                DefaultValue = "same as --cpu-strict",
                AllowedValues = new() { "0", "1" },
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-prio",
                Aliases = new() { "--spec-draft-prio", "--prio-draft" },
                DescriptionEn = "Set draft model process/thread priority",
                DescriptionRu = "Приоритет процесса/потока черновой модели",
                DefaultValue = "0",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-poll",
                Aliases = new() { "--spec-draft-poll", "--poll-draft" },
                DescriptionEn = "Use polling to wait for draft model work",
                DescriptionRu = "Использовать опрос для ожидания работы черновой модели",
                DefaultValue = "same as --poll",
                AllowedValues = new() { "0", "1" },
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-Cbd",
                Aliases = new() { "--spec-draft-cpu-mask-batch", "-Cbd", "--cpu-mask-batch-draft" },
                DescriptionEn = "Draft model CPU affinity mask for batch processing",
                DescriptionRu = "Маска привязки CPU для пакетной обработки черновой модели",
                DefaultValue = "same as --cpu-mask",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-cpu-strict-batch",
                Aliases = new() { "--spec-draft-cpu-strict-batch", "--cpu-strict-batch-draft" },
                DescriptionEn = "Use strict CPU placement for the draft model during batch processing",
                DescriptionRu = "Строгое размещение CPU для черновой модели при пакетной обработке",
                DefaultValue = "same as --spec-draft-cpu-strict",
                AllowedValues = new() { "0", "1" },
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-prio-batch",
                Aliases = new() { "--spec-draft-prio-batch", "--prio-batch-draft" },
                DescriptionEn = "Draft model process/thread priority for batch processing: 0-normal, 1-medium, 2-high, 3-realtime",
                DescriptionRu = "Приоритет процесса/потока черновой модели для пакетной обработки: 0-normal, 1-medium, 2-high, 3-realtime",
                DefaultValue = "0",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-poll-batch",
                Aliases = new() { "--spec-draft-poll-batch", "--poll-batch-draft" },
                DescriptionEn = "Use polling to wait for draft model work during batch processing",
                DescriptionRu = "Использовать опрос для ожидания работы черновой модели при пакетной обработке",
                DefaultValue = "same as --spec-draft-poll",
                AllowedValues = new() { "0", "1" },
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-override-tensor",
                Aliases = new() { "--spec-draft-override-tensor", "-otd", "--override-tensor-draft" },
                DescriptionEn = "Override tensor buffer type for draft model",
                DescriptionRu = "Переопределить тип буфера тензоров для черновой модели",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-cmoed",
                Aliases = new() { "--spec-draft-cpu-moe", "-cmoed", "--cpu-moe-draft" },
                DescriptionEn = "Keep all MoE weights in the CPU for the draft model",
                DescriptionRu = "Хранить все веса MoE в CPU для черновой модели",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-ncmoed",
                Aliases = new() { "--spec-draft-n-cpu-moe", "--spec-draft-ncmoe", "-ncmoed", "--n-cpu-moe-draft" },
                DescriptionEn = "Keep the MoE weights of the first N layers in the CPU for the draft model",
                DescriptionRu = "Хранить веса MoE первых N слоёв в CPU для черновой модели",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-n-max",
                Aliases = new() { "--spec-draft-n-max" },
                DescriptionEn = "Number of tokens to draft for speculative decoding",
                DescriptionRu = "Количество токенов для чернового декодирования",
                DefaultValue = "3",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-n-min",
                Aliases = new() { "--spec-draft-n-min" },
                DescriptionEn = "Minimum number of draft tokens to use for speculative decoding",
                DescriptionRu = "Минимальное число черновых токенов для спекулятивного декодирования",
                DefaultValue = "0",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-p-split",
                Aliases = new() { "--spec-draft-p-split", "--draft-p-split" },
                DescriptionEn = "Speculative decoding split probability",
                DescriptionRu = "Вероятность разделения при спекулятивном декодировании",
                DefaultValue = "0.10",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-p-min",
                Aliases = new() { "--spec-draft-p-min", "--draft-p-min" },
                DescriptionEn = "Minimum speculative decoding probability (greedy)",
                DescriptionRu = "Минимальная вероятность спекулятивного декодирования (жадное)",
                DefaultValue = "0.00",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-n-ubatch",
                Aliases = new() { "--spec-draft-n-ubatch" },
                DescriptionEn = "Physical maximum batch size of the draft context; a smaller value spares VRAM (0 = same as --ubatch-size)",
                DescriptionRu = "Физический максимальный размер пакета контекста черновой модели; меньшее значение экономит VRAM (0 = как --ubatch-size)",
                DefaultValue = "0",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-sampling",
                Aliases = new() { "--spec-draft-sampling" },
                DescriptionEn = "How the draft is sampled: greedy takes its argmax, probabilistic samples it and the target model verifies it by rejection sampling",
                DescriptionRu = "Как выбираются черновые токены: greedy берёт самый вероятный, probabilistic сэмплирует, а целевая модель проверяет их через rejection sampling",
                DefaultValue = "greedy",
                AllowedValues = new() { "greedy", "probabilistic" },
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-synth-len",
                Aliases = new() { "--spec-synth-len" },
                DescriptionEn = "Target mean synthetic acceptance length, including the target token (benchmarking only)",
                DescriptionRu = "Целевая средняя длина синтетического принятия, включая целевой токен (только для бенчмарков)",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-synth-rates",
                Aliases = new() { "--spec-synth-rates" },
                DescriptionEn = "Comma-separated unconditional per-position synthetic acceptance probabilities (benchmarking only)",
                DescriptionRu = "Безусловные вероятности синтетического принятия по позициям через запятую (только для бенчмарков)",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-draft-backend-sampling",
                Aliases = new() { "--spec-draft-backend-sampling", "--no-spec-draft-backend-sampling" },
                DescriptionEn = "Offload draft sampling to the backend",
                DescriptionRu = "Офлоадить сэмплирование черновика на бэкенд",
                DefaultValue = "enabled",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-devd",
                Aliases = new() { "--spec-draft-device", "-devd", "--device-draft" },
                DescriptionEn = "Comma-separated list of devices to use for offloading the draft model",
                DescriptionRu = "Список устройств для офлоада черновой модели через запятую",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-ngld",
                Aliases = new() { "--spec-draft-ngl", "-ngld", "--gpu-layers-draft", "--n-gpu-layers-draft" },
                DescriptionEn = "Max number of draft model layers to store in VRAM ('auto', 'all', or exact number)",
                DescriptionRu = "Макс. количество слоёв черновой модели в VRAM ('auto', 'all' или точное число)",
                DefaultValue = "auto",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "-md",
                Aliases = new() { "--spec-draft-model", "-md", "--model-draft" },
                DescriptionEn = "Draft model for speculative decoding",
                DescriptionRu = "Черновая модель для спекулятивного декодирования",
                DefaultValue = "unused",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-type",
                Aliases = new() { "--spec-type" },
                DescriptionEn = "Comma-separated list of types of speculative decoding to use",
                DescriptionRu = "Список типов спекулятивного декодирования через запятую",
                DefaultValue = "none",
                AllowedValues = new()
                {
                    "none", "draft-simple", "draft-eagle3", "draft-mtp",
                    "ngram-simple", "ngram-map-k", "ngram-map-k4v", "ngram-mod", "ngram-cache"
                },
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-mod-n-min",
                Aliases = new() { "--spec-ngram-mod-n-min" },
                DescriptionEn = "Minimum number of ngram tokens for ngram-mod speculative decoding",
                DescriptionRu = "Минимальное число ngram-токенов для ngram-mod спекулятивного декодирования",
                DefaultValue = "48",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-mod-n-max",
                Aliases = new() { "--spec-ngram-mod-n-max" },
                DescriptionEn = "Maximum number of ngram tokens for ngram-mod speculative decoding",
                DescriptionRu = "Максимальное число ngram-токенов для ngram-mod спекулятивного декодирования",
                DefaultValue = "64",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-mod-n-match",
                Aliases = new() { "--spec-ngram-mod-n-match" },
                DescriptionEn = "Ngram-mod lookup length",
                DescriptionRu = "Длина поиска ngram-mod",
                DefaultValue = "24",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-simple-size-n",
                Aliases = new() { "--spec-ngram-simple-size-n" },
                DescriptionEn = "Ngram size N for ngram-simple speculative decoding (lookup n-gram length)",
                DescriptionRu = "Размер N ngram для ngram-simple (длина поиска n-gram)",
                DefaultValue = "12",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-simple-size-m",
                Aliases = new() { "--spec-ngram-simple-size-m" },
                DescriptionEn = "Ngram size M for ngram-simple speculative decoding (draft m-gram length)",
                DescriptionRu = "Размер M ngram для ngram-simple (длина чернового m-gram)",
                DefaultValue = "48",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-simple-min-hits",
                Aliases = new() { "--spec-ngram-simple-min-hits" },
                DescriptionEn = "Minimum hits for ngram-simple speculative decoding",
                DescriptionRu = "Минимальное число совпадений для ngram-simple",
                DefaultValue = "1",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-map-k-size-n",
                Aliases = new() { "--spec-ngram-map-k-size-n" },
                DescriptionEn = "Ngram size N for ngram-map-k speculative decoding",
                DescriptionRu = "Размер N ngram для ngram-map-k",
                DefaultValue = "12",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-map-k-size-m",
                Aliases = new() { "--spec-ngram-map-k-size-m" },
                DescriptionEn = "Ngram size M for ngram-map-k speculative decoding",
                DescriptionRu = "Размер M ngram для ngram-map-k",
                DefaultValue = "48",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-map-k-min-hits",
                Aliases = new() { "--spec-ngram-map-k-min-hits" },
                DescriptionEn = "Minimum hits for ngram-map-k speculative decoding",
                DescriptionRu = "Минимальное число совпадений для ngram-map-k",
                DefaultValue = "1",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-map-k4v-size-n",
                Aliases = new() { "--spec-ngram-map-k4v-size-n" },
                DescriptionEn = "Ngram size N for ngram-map-k4v speculative decoding",
                DescriptionRu = "Размер N ngram для ngram-map-k4v",
                DefaultValue = "12",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-map-k4v-size-m",
                Aliases = new() { "--spec-ngram-map-k4v-size-m" },
                DescriptionEn = "Ngram size M for ngram-map-k4v speculative decoding",
                DescriptionRu = "Размер M ngram для ngram-map-k4v",
                DefaultValue = "48",
                Category = "speculative"
            },
            new()
            {
                PrimaryFlag = "--spec-ngram-map-k4v-min-hits",
                Aliases = new() { "--spec-ngram-map-k4v-min-hits" },
                DescriptionEn = "Minimum hits for ngram-map-k4v speculative decoding",
                DescriptionRu = "Минимальное число совпадений для ngram-map-k4v",
                DefaultValue = "1",
                Category = "speculative"
            },

            // ===== example-specific (server) params =====

            new()
            {
                PrimaryFlag = "-lcs",
                Aliases = new() { "-lcs", "--lookup-cache-static" },
                DescriptionEn = "Path to static lookup cache for lookup decoding (not updated by generation)",
                DescriptionRu = "Путь к статическому кэшу поиска для lookup-декодирования (не обновляется при генерации)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-lcd",
                Aliases = new() { "-lcd", "--lookup-cache-dynamic" },
                DescriptionEn = "Path to dynamic lookup cache for lookup decoding (updated by generation)",
                DescriptionRu = "Путь к динамическому кэшу поиска для lookup-декодирования (обновляется при генерации)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-ctxcp",
                Aliases = new() { "-ctxcp", "--ctx-checkpoints", "--swa-checkpoints" },
                DescriptionEn = "Max number of context checkpoints to create per slot",
                DescriptionRu = "Максимальное количество контрольных точек контекста на слот",
                DefaultValue = "32",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-cms",
                Aliases = new() { "-cms", "--checkpoint-min-step" },
                DescriptionEn = "Minimum spacing between context checkpoints in tokens (0 = no minimum)",
                DescriptionRu = "Минимальный интервал между контрольными точками контекста в токенах (0 = без минимума)",
                DefaultValue = "8192",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-cram",
                Aliases = new() { "-cram", "--cache-ram" },
                DescriptionEn = "Set the maximum cache size in MiB (-1 = no limit, 0 = disable)",
                DescriptionRu = "Максимальный размер кэша в МиБ (-1 = без ограничений, 0 = отключить)",
                DefaultValue = "8192",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-kvu",
                Aliases = new() { "-kvu", "--kv-unified", "-no-kvu", "--no-kv-unified" },
                DescriptionEn = "Use single unified KV buffer shared across all sequences",
                DescriptionRu = "Использовать единый буфер KV для всех последовательностей",
                DefaultValue = "enabled if slots is auto",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cache-idle-slots",
                Aliases = new() { "--cache-idle-slots", "--no-cache-idle-slots" },
                DescriptionEn = "Save and clear idle slots on new task",
                DescriptionRu = "Сохранять и очищать незанятые слоты при новой задаче",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--context-shift",
                Aliases = new() { "--context-shift", "--no-context-shift" },
                DescriptionEn = "Whether to use context shift on infinite text generation",
                DescriptionRu = "Использовать сдвиг контекста при бесконечной генерации текста",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-r",
                Aliases = new() { "-r", "--reverse-prompt" },
                DescriptionEn = "Halt generation at PROMPT, return control in interactive mode",
                DescriptionRu = "Остановить генерацию на PROMPT, вернуть управление в интерактивном режиме",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-sp",
                Aliases = new() { "-sp", "--special" },
                DescriptionEn = "Special tokens output enabled",
                DescriptionRu = "Вывод специальных токенов включён",
                DefaultValue = "false",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--warmup",
                Aliases = new() { "--warmup", "--no-warmup" },
                DescriptionEn = "Whether to perform warmup with an empty run",
                DescriptionRu = "Выполнять ли прогревочный пустой запуск",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--spm-infill",
                Aliases = new() { "--spm-infill" },
                DescriptionEn = "Use Suffix/Prefix/Middle pattern for infill (default: Prefix/Suffix/Middle)",
                DescriptionRu = "Использовать шаблон Суффикс/Префикс/Середина для infill (по умолчанию: Префикс/Суффикс/Середина)",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--pooling",
                Aliases = new() { "--pooling" },
                DescriptionEn = "Pooling type for embeddings, use model default if unspecified",
                DescriptionRu = "Тип пулинга для эмбеддингов, по умолчанию берётся из модели",
                AllowedValues = new() { "none", "mean", "cls", "last", "rank" },
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-np",
                Aliases = new() { "-np", "--parallel" },
                DescriptionEn = "Number of server slots (-1 = auto)",
                DescriptionRu = "Количество слотов сервера (-1 = автоматически)",
                DefaultValue = "-1",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--kv-unified-per-slot",
                Aliases = new() { "--kv-unified-per-slot" },
                DescriptionEn = "Context limit per parallel slot; without -c the shared KV cache is sized to slots x N",
                DescriptionRu = "Предел контекста на один параллельный слот; без -c общий KV-кэш выделяется на слоты x N",
                DefaultValue = "unset",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-cb",
                Aliases = new() { "-cb", "--cont-batching", "-nocb", "--no-cont-batching" },
                DescriptionEn = "Whether to enable continuous batching (a.k.a dynamic batching)",
                DescriptionRu = "Включить непрерывное батчирование (динамическое батчирование)",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-mm",
                Aliases = new() { "-mm", "--mmproj" },
                DescriptionEn = "Path to a multimodal projector file",
                DescriptionRu = "Путь к файлу мультимодального проектора",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-mmu",
                Aliases = new() { "-mmu", "--mmproj-url" },
                DescriptionEn = "URL to a multimodal projector file",
                DescriptionRu = "URL файла мультимодального проектора",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--mmproj-auto",
                Aliases = new() { "--mmproj-auto", "--no-mmproj", "--no-mmproj-auto" },
                DescriptionEn = "Whether to use multimodal projector file (if available), useful when using -hf",
                DescriptionRu = "Использовать ли файл мультимодального проектора (при наличии), полезно с -hf",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--mmproj-offload",
                Aliases = new() { "--mmproj-offload", "--no-mmproj-offload" },
                DescriptionEn = "Whether to enable GPU offloading for multimodal projector",
                DescriptionRu = "Включить офлоад мультимодального проектора на GPU",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-mmdev",
                Aliases = new() { "-mmdev", "--mmproj-device" },
                DescriptionEn = "Device for the multimodal projector (none = do not offload); follows --device by default. See --list-devices",
                DescriptionRu = "Устройство для мультимодального проектора (none = не выгружать на GPU); по умолчанию как --device. Список устройств: --list-devices",
                DefaultValue = "same as --device",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--mtmd-batch-max-tokens",
                Aliases = new() { "--mtmd-batch-max-tokens" },
                DescriptionEn = "Maximum number of image tokens per batch when encoding images",
                DescriptionRu = "Максимум токенов изображения в одном пакете при кодировании изображений",
                DefaultValue = "1024",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--image-min-tokens",
                Aliases = new() { "--image-min-tokens" },
                DescriptionEn = "Minimum number of tokens each image can take (vision models with dynamic resolution)",
                DescriptionRu = "Минимальное количество токенов на изображение (визуальные модели с динамическим разрешением)",
                DefaultValue = "read from model",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--image-max-tokens",
                Aliases = new() { "--image-max-tokens" },
                DescriptionEn = "Maximum number of tokens each image can take (vision models with dynamic resolution)",
                DescriptionRu = "Максимальное количество токенов на изображение (визуальные модели с динамическим разрешением)",
                DefaultValue = "read from model",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--video-fps",
                Aliases = new() { "--video-fps" },
                DescriptionEn = "Target frame rate for video input",
                DescriptionRu = "Целевая частота кадров для входного видео",
                DefaultValue = "4.0",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--video-timestamp-interval",
                Aliases = new() { "--video-timestamp-interval" },
                DescriptionEn = "Interval in milliseconds between text timestamps in video input",
                DescriptionRu = "Интервал в миллисекундах между текстовыми метками времени во входном видео",
                DefaultValue = "5000",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--video-ffmpeg-dir",
                Aliases = new() { "--video-ffmpeg-dir" },
                DescriptionEn = "Directory with ffmpeg and ffprobe for video input; searched in PATH by default",
                DescriptionRu = "Папка с ffmpeg и ffprobe для входного видео; по умолчанию они ищутся в PATH",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-a",
                Aliases = new() { "-a", "--alias" },
                DescriptionEn = "Set model name aliases, comma-separated (to be used by API)",
                DescriptionRu = "Установить псевдонимы модели через запятую (используются API)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--tags",
                Aliases = new() { "--tags" },
                DescriptionEn = "Set model tags, comma-separated (informational, not used for routing)",
                DescriptionRu = "Установить теги модели через запятую (информационные, не используются для маршрутизации)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--embd-normalize",
                Aliases = new() { "--embd-normalize" },
                DescriptionEn = "Normalisation for embeddings (-1=none, 0=max absolute int16, 1=taxicab, 2=euclidean, >2=p-norm)",
                DescriptionRu = "Нормализация эмбеддингов (-1=нет, 0=max abs int16, 1=taxicab, 2=евклидова, >2=p-норма)",
                DefaultValue = "2",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--host",
                Aliases = new() { "--host" },
                DescriptionEn = "IP addresses to listen on, comma-separated, or UNIX socket paths ending in .sock",
                DescriptionRu = "IP-адреса для прослушивания через запятую или пути UNIX-сокетов, оканчивающиеся на .sock",
                DefaultValue = "127.0.0.1",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--port",
                Aliases = new() { "--port" },
                DescriptionEn = "Port to listen",
                DescriptionRu = "Порт для прослушивания",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--reuse-port",
                Aliases = new() { "--reuse-port" },
                DescriptionEn = "Allow multiple sockets to bind to the same port",
                DescriptionRu = "Разрешить нескольким сокетам привязываться к одному порту",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--path",
                Aliases = new() { "--path" },
                DescriptionEn = "Path to serve static files from",
                DescriptionRu = "Путь для раздачи статических файлов",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cors-origins",
                Aliases = new() { "--cors-origins" },
                DescriptionEn = "Comma-separated list of allowed CORS origins; localhost reflects the Origin header only when it is localhost",
                DescriptionRu = "Разрешённые источники CORS через запятую; значение localhost возвращает заголовок Origin, только если это localhost",
                DefaultValue = "*",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cors-methods",
                Aliases = new() { "--cors-methods" },
                DescriptionEn = "Comma-separated list of allowed CORS methods",
                DescriptionRu = "Разрешённые методы CORS через запятую",
                DefaultValue = "GET,POST,DELETE,OPTIONS",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cors-headers",
                Aliases = new() { "--cors-headers" },
                DescriptionEn = "Comma-separated list of allowed CORS headers",
                DescriptionRu = "Разрешённые заголовки CORS через запятую",
                DefaultValue = "*",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cors-credentials",
                Aliases = new() { "--cors-credentials", "--no-cors-credentials" },
                DescriptionEn = "Allow credentials for CORS. With --cors-origins left at *, the Origin header is echoed back and credentials are always allowed",
                DescriptionRu = "Разрешить учётные данные для CORS. Если --cors-origins оставлен *, заголовок Origin возвращается как есть и учётные данные разрешены всегда",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--api-prefix",
                Aliases = new() { "--api-prefix" },
                DescriptionEn = "Prefix path the server serves from, without the trailing slash",
                DescriptionRu = "Префикс пути сервера без завершающего слеша",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--ui-config",
                Aliases = new() { "--ui-config", "--webui-config" },
                DescriptionEn = "JSON that provides default UI settings (overrides UI defaults)",
                DescriptionRu = "JSON с настройками UI по умолчанию (переопределяет стандартные)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--ui-config-file",
                Aliases = new() { "--ui-config-file", "--webui-config-file" },
                DescriptionEn = "JSON file that provides default UI settings",
                DescriptionRu = "JSON-файл с настройками UI по умолчанию",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--ui-mcp-proxy",
                Aliases = new() { "--ui-mcp-proxy", "--no-ui-mcp-proxy", "--webui-mcp-proxy", "--no-webui-mcp-proxy" },
                DescriptionEn = "Experimental: enable MCP CORS proxy (do not enable in untrusted environments)",
                DescriptionRu = "Экспериментальное: прокси MCP CORS (не включать в недоверенных средах)",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--tools",
                Aliases = new() { "--tools" },
                DescriptionEn = "Experimental: enable built-in tools for AI agents ('all' to enable all)",
                DescriptionRu = "Экспериментальное: встроенные инструменты для AI-агентов ('all' для включения всех)",
                DefaultValue = "no tools",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--tools-runtime",
                Aliases = new() { "--tools-runtime" },
                DescriptionEn = "Experimental: run tools in a separate runtime: docker:<image>, podman:<image>, docker-container:<id>, podman-container:<id>, ssh:<target>",
                DescriptionRu = "Экспериментальное: запуск инструментов в отдельной среде: docker:<образ>, podman:<образ>, docker-container:<id>, podman-container:<id>, ssh:<хост>",
                DefaultValue = "none",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--mcp-servers-config",
                Aliases = new() { "--mcp-servers-config" },
                DescriptionEn = "Experimental: path to a JSON file with MCP server definitions (Cursor-compatible format). Limits --cors-origins to localhost by default",
                DescriptionRu = "Экспериментальное: путь к JSON-файлу с описанием MCP-серверов (формат Cursor). По умолчанию ограничивает --cors-origins до localhost",
                DefaultValue = "none",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--mcp-servers-json",
                Aliases = new() { "--mcp-servers-json" },
                DescriptionEn = "Experimental: inline JSON with MCP server definitions (Cursor-compatible format). Limits --cors-origins to localhost by default",
                DescriptionRu = "Экспериментальное: JSON с описанием MCP-серверов прямо в аргументе (формат Cursor). По умолчанию ограничивает --cors-origins до localhost",
                DefaultValue = "none",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-ag",
                Aliases = new() { "-ag", "--agent", "-no-ag", "--no-agent" },
                DescriptionEn = "Enable the CORS proxy and all built-in tools at once (equivalent to --tools all plus --ui-mcp-proxy)",
                DescriptionRu = "Включить CORS-прокси и все встроенные инструменты сразу (эквивалент --tools all вместе с --ui-mcp-proxy)",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--connect",
                Aliases = new() { "--connect" },
                DescriptionEn = "Experimental: open a peer-to-peer tunnel to the server through llama-connect, so it can be reached from outside the local network. Anyone with the share code can use the server",
                DescriptionRu = "Экспериментально: открыть p2p-туннель к серверу через llama-connect, чтобы до него можно было достучаться извне локальной сети. Пользоваться сервером сможет любой, у кого есть код",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--connect-code",
                Aliases = new() { "--connect-code" },
                DescriptionEn = "40-character code for --connect; a new one is generated for each run by default",
                DescriptionRu = "Код из 40 символов для --connect; по умолчанию при каждом запуске создаётся новый",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--webui",
                Aliases = new() { "--webui", "--no-webui", "--ui", "--no-ui" },
                DescriptionEn = "Whether to enable the Web UI",
                DescriptionRu = "Включить веб-интерфейс",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--embedding",
                Aliases = new() { "--embedding", "--embeddings" },
                DescriptionEn = "Restrict to only support embedding use case; use only with dedicated embedding models",
                DescriptionRu = "Ограничить только эмбеддинги; использовать только со специализированными моделями",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--rerank",
                Aliases = new() { "--rerank", "--reranking" },
                DescriptionEn = "Enable reranking endpoint on server",
                DescriptionRu = "Включить эндпоинт реранжирования на сервере",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--api-key",
                Aliases = new() { "--api-key" },
                DescriptionEn = "API key for authentication (comma-separated for multiple keys)",
                DescriptionRu = "API-ключ для аутентификации (через запятую для нескольких ключей)",
                DefaultValue = "none",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--api-key-file",
                Aliases = new() { "--api-key-file" },
                DescriptionEn = "Path to file containing API keys",
                DescriptionRu = "Путь к файлу с API-ключами",
                DefaultValue = "none",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--ssl-key-file",
                Aliases = new() { "--ssl-key-file" },
                DescriptionEn = "Path to a PEM-encoded SSL private key",
                DescriptionRu = "Путь к PEM-закодированному SSL приватному ключу",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--ssl-cert-file",
                Aliases = new() { "--ssl-cert-file" },
                DescriptionEn = "Path to a PEM-encoded SSL certificate",
                DescriptionRu = "Путь к PEM-закодированному SSL сертификату",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--chat-template-kwargs",
                Aliases = new() { "--chat-template-kwargs" },
                DescriptionEn = "Additional params for the json template parser (JSON object string)",
                DescriptionRu = "Дополнительные параметры для парсера шаблонов JSON (строка JSON-объекта)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-to",
                Aliases = new() { "-to", "--timeout" },
                DescriptionEn = "Server read/write timeout in seconds",
                DescriptionRu = "Таймаут чтения/записи сервера в секундах",
                DefaultValue = "3600",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--sse-ping-interval",
                Aliases = new() { "--sse-ping-interval" },
                DescriptionEn = "SSE ping interval in seconds (-1 = disabled)",
                DescriptionRu = "Интервал SSE-пингов в секундах (-1 = выкл)",
                DefaultValue = "30",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--threads-http",
                Aliases = new() { "--threads-http" },
                DescriptionEn = "Number of threads used to process HTTP requests",
                DescriptionRu = "Количество потоков для обработки HTTP-запросов",
                DefaultValue = "-1",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cache-prompt",
                Aliases = new() { "--cache-prompt", "--no-cache-prompt" },
                DescriptionEn = "Whether to enable prompt caching",
                DescriptionRu = "Включить кэширование промптов",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--cache-reuse",
                Aliases = new() { "--cache-reuse" },
                DescriptionEn = "Min chunk size to attempt reusing from the cache via KV shifting (requires prompt caching)",
                DescriptionRu = "Минимальный размер блока для повторного использования из кэша через KV-сдвиг (требует кэширования промптов)",
                DefaultValue = "0",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--metrics",
                Aliases = new() { "--metrics" },
                DescriptionEn = "Enable prometheus compatible metrics endpoint",
                DescriptionRu = "Включить эндпоинт метрик, совместимый с Prometheus",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--props",
                Aliases = new() { "--props" },
                DescriptionEn = "Enable changing global properties via POST /props",
                DescriptionRu = "Включить изменение глобальных свойств через POST /props",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--slots",
                Aliases = new() { "--slots", "--no-slots" },
                DescriptionEn = "Expose slots monitoring endpoint",
                DescriptionRu = "Открыть эндпоинт мониторинга слотов",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--slot-save-path",
                Aliases = new() { "--slot-save-path" },
                DescriptionEn = "Path to save slot KV cache",
                DescriptionRu = "Путь для сохранения KV-кэша слота",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--media-path",
                Aliases = new() { "--media-path" },
                DescriptionEn = "Directory for loading local media files (accessed via file:// URLs)",
                DescriptionRu = "Директория для загрузки локальных медиафайлов (доступ через file:// URL)",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--models-dir",
                Aliases = new() { "--models-dir" },
                DescriptionEn = "Directory containing models for the router server",
                DescriptionRu = "Директория с моделями для сервера-маршрутизатора",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--models-preset",
                Aliases = new() { "--models-preset" },
                DescriptionEn = "Path to INI file containing model presets for the router server",
                DescriptionRu = "Путь к INI-файлу с пресетами моделей для сервера-маршрутизатора",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--models-max",
                Aliases = new() { "--models-max" },
                DescriptionEn = "Maximum number of models to load simultaneously for router (0 = unlimited)",
                DescriptionRu = "Макс. количество одновременно загруженных моделей для маршрутизатора (0 = без ограничений)",
                DefaultValue = "4",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--models-memory-margin",
                Aliases = new() { "--models-memory-margin" },
                DescriptionEn = "Router mode: MiB of device memory to keep free on each device; least recently used models are unloaded until a new one fits (0 = disabled)",
                DescriptionRu = "Режим роутера: сколько МиБ памяти держать свободными на каждом устройстве; давно не использованные модели выгружаются, пока новая не поместится (0 = выкл)",
                DefaultValue = "0",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--models-autoload",
                Aliases = new() { "--models-autoload", "--no-models-autoload" },
                DescriptionEn = "Whether to automatically load models for router server",
                DescriptionRu = "Автоматически загружать модели для сервера-маршрутизатора",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--jinja",
                Aliases = new() { "--jinja", "--no-jinja" },
                DescriptionEn = "Whether to use jinja template engine for chat",
                DescriptionRu = "Использовать шаблонизатор jinja для чата",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--reasoning-format",
                Aliases = new() { "--reasoning-format" },
                DescriptionEn = "Controls thought tag handling and extraction format (none, deepseek, deepseek-legacy)",
                DescriptionRu = "Управление тегами мыслей и форматом извлечения (none, deepseek, deepseek-legacy)",
                DefaultValue = "auto",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-rea",
                Aliases = new() { "-rea", "--reasoning" },
                DescriptionEn = "Use reasoning/thinking in the chat ('on', 'off', or 'auto')",
                DescriptionRu = "Использовать рассуждения/мышление в чате ('on', 'off' или 'auto')",
                DefaultValue = "auto",
                AllowedValues = new() { "on", "off", "auto" },
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--reasoning-effort",
                Aliases = new() { "--reasoning-effort" },
                DescriptionEn = "Reasoning effort level for the chat template: default keeps the template default, or a level such as minimal, low, medium, high, xhigh or max",
                DescriptionRu = "Уровень рассуждений для шаблона чата: default оставляет умолчание шаблона, иначе уровень вроде minimal, low, medium, high, xhigh или max",
                DefaultValue = "default",
                AllowedValues = new() { "default", "minimal", "low", "medium", "high", "xhigh", "max" },
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--reasoning-budget",
                Aliases = new() { "--reasoning-budget" },
                DescriptionEn = "Token budget for thinking: -1 = unrestricted, 0 = immediate end, N>0 = token budget",
                DescriptionRu = "Бюджет токенов для мышления: -1 = без ограничений, 0 = немедленный конец, N>0 = бюджет",
                DefaultValue = "-1",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--reasoning-budget-message",
                Aliases = new() { "--reasoning-budget-message" },
                DescriptionEn = "Message injected before the end-of-thinking tag when budget is exhausted",
                DescriptionRu = "Сообщение, вставляемое перед закрывающим тегом мышления при исчерпании бюджета",
                DefaultValue = "none",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--reasoning-preserve",
                Aliases = new() { "--reasoning-preserve", "--no-reasoning-preserve" },
                DescriptionEn = "Keep the reasoning trace in the whole chat history, not only in the last assistant message; used by templates that support it",
                DescriptionRu = "Сохранять рассуждения во всей истории чата, а не только в последнем ответе ассистента; учитывается шаблонами, которые это поддерживают",
                DefaultValue = "enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--chat-template",
                Aliases = new() { "--chat-template" },
                DescriptionEn = "Set custom jinja chat template (default: from model metadata)",
                DescriptionRu = "Установить пользовательский jinja-шаблон чата (по умолчанию: из метаданных модели)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--chat-template-file",
                Aliases = new() { "--chat-template-file" },
                DescriptionEn = "Set custom jinja chat template file",
                DescriptionRu = "Установить файл пользовательского jinja-шаблона чата",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--skip-chat-parsing",
                Aliases = new() { "--skip-chat-parsing", "--no-skip-chat-parsing" },
                DescriptionEn = "Force a pure content parser even if a Jinja template is specified",
                DescriptionRu = "Принудительно использовать чистый парсер контента даже при наличии Jinja-шаблона",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--prefill-assistant",
                Aliases = new() { "--prefill-assistant", "--no-prefill-assistant" },
                DescriptionEn = "Whether to prefill the assistant's response if the last message is an assistant message",
                DescriptionRu = "Выполнять ли предзаполнение ответа ассистента, если последнее сообщение — от ассистента",
                DefaultValue = "prefill enabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "-sps",
                Aliases = new() { "-sps", "--slot-prompt-similarity" },
                DescriptionEn = "How much the prompt must match the slot prompt to reuse (0.0 = disabled)",
                DescriptionRu = "Насколько промпт должен совпадать с промптом слота для повторного использования (0.0 = выкл)",
                DefaultValue = "0.10",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--lora-init-without-apply",
                Aliases = new() { "--lora-init-without-apply" },
                DescriptionEn = "Load LoRA adapters without applying them (apply later via POST /lora-adapters)",
                DescriptionRu = "Загрузить адаптеры LoRA без применения (применить позже через POST /lora-adapters)",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--sleep-idle-seconds",
                Aliases = new() { "--sleep-idle-seconds" },
                DescriptionEn = "Seconds of idleness after which the server will sleep (-1 = disabled)",
                DescriptionRu = "Секунды бездействия, после которых сервер засыпает (-1 = отключено)",
                DefaultValue = "-1",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--log-prompts-dir",
                Aliases = new() { "--log-prompts-dir" },
                DescriptionEn = "Directory to log prompts to, created if missing; for debugging only",
                DescriptionRu = "Папка для записи промптов, создаётся при необходимости; только для отладки",
                DefaultValue = "disabled",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--embd-gemma-default",
                Aliases = new() { "--embd-gemma-default" },
                DescriptionEn = "Use default EmbeddingGemma model (may download weights from the internet)",
                DescriptionRu = "Использовать модель EmbeddingGemma по умолчанию (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--fim-qwen-1.5b-default",
                Aliases = new() { "--fim-qwen-1.5b-default" },
                DescriptionEn = "Use default Qwen 2.5 Coder 1.5B (may download weights from the internet)",
                DescriptionRu = "Использовать Qwen 2.5 Coder 1.5B по умолчанию (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--fim-qwen-3b-default",
                Aliases = new() { "--fim-qwen-3b-default" },
                DescriptionEn = "Use default Qwen 2.5 Coder 3B (may download weights from the internet)",
                DescriptionRu = "Использовать Qwen 2.5 Coder 3B по умолчанию (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--fim-qwen-7b-default",
                Aliases = new() { "--fim-qwen-7b-default" },
                DescriptionEn = "Use default Qwen 2.5 Coder 7B (may download weights from the internet)",
                DescriptionRu = "Использовать Qwen 2.5 Coder 7B по умолчанию (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--fim-qwen-7b-spec",
                Aliases = new() { "--fim-qwen-7b-spec" },
                DescriptionEn = "Use Qwen 2.5 Coder 7B + 0.5B draft for speculative decoding",
                DescriptionRu = "Использовать Qwen 2.5 Coder 7B + 0.5B черновик для спекулятивного декодирования",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--fim-qwen-14b-spec",
                Aliases = new() { "--fim-qwen-14b-spec" },
                DescriptionEn = "Use Qwen 2.5 Coder 14B + 0.5B draft for speculative decoding",
                DescriptionRu = "Использовать Qwen 2.5 Coder 14B + 0.5B черновик для спекулятивного декодирования",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--fim-qwen-30b-default",
                Aliases = new() { "--fim-qwen-30b-default" },
                DescriptionEn = "Use default Qwen 3 Coder 30B A3B Instruct (may download weights)",
                DescriptionRu = "Использовать Qwen 3 Coder 30B A3B Instruct по умолчанию (может скачать веса)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--gpt-oss-20b-default",
                Aliases = new() { "--gpt-oss-20b-default" },
                DescriptionEn = "Use gpt-oss-20b (may download weights from the internet)",
                DescriptionRu = "Использовать gpt-oss-20b (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--gpt-oss-120b-default",
                Aliases = new() { "--gpt-oss-120b-default" },
                DescriptionEn = "Use gpt-oss-120b (may download weights from the internet)",
                DescriptionRu = "Использовать gpt-oss-120b (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--vision-gemma-4b-default",
                Aliases = new() { "--vision-gemma-4b-default" },
                DescriptionEn = "Use Gemma 3 4B QAT (may download weights from the internet)",
                DescriptionRu = "Использовать Gemma 3 4B QAT (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--vision-gemma-12b-default",
                Aliases = new() { "--vision-gemma-12b-default" },
                DescriptionEn = "Use Gemma 3 12B QAT (may download weights from the internet)",
                DescriptionRu = "Использовать Gemma 3 12B QAT (может скачать веса из интернета)",
                Category = "server"
            },
            new()
            {
                PrimaryFlag = "--spec-default",
                Aliases = new() { "--spec-default" },
                DescriptionEn = "Enable default speculative decoding config",
                DescriptionRu = "Включить конфигурацию спекулятивного декодирования по умолчанию",
                Category = "server"
            },
        };

        foreach (var def in defs)
        {
            _all.Add(def);
            _byFlag[def.PrimaryFlag] = def;
            foreach (var alias in def.Aliases)
            {
                if (!_byFlag.ContainsKey(alias))
                    _byFlag[alias] = def;
            }
        }
    }

    public static LlamaArgumentDefinition? GetDefinition(string flag)
    {
        return _byFlag.TryGetValue(flag, out var def) ? def : null;
    }

    public static string GetLocalizedDescription(LlamaArgumentDefinition def)
    {
        var culture = LocalizedStrings.CurrentCulture;
        if (culture != null && culture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
            return def.DescriptionRu;
        return def.DescriptionEn;
    }

    public static string GetLocalizedDescription(string flag)
    {
        var def = GetDefinition(flag);
        return def != null ? GetLocalizedDescription(def) : "";
    }

    public static IReadOnlyList<LlamaArgumentDefinition> AllDefinitions => _all;

    public static string GetCategoryDisplayName(string category)
    {
        var culture = LocalizedStrings.CurrentCulture;
        bool isRu = culture != null && culture.Name.StartsWith("ru", StringComparison.OrdinalIgnoreCase);

        return category switch
        {
            "common" => isRu ? "Общие параметры" : "Common Parameters",
            "sampling" => isRu ? "Параметры сэмплирования" : "Sampling Parameters",
            "speculative" => isRu ? "Спекулятивное декодирование" : "Speculative Decoding",
            "server" => isRu ? "Параметры сервера" : "Server Parameters",
            "other" => isRu ? "Прочие параметры" : "Other Parameters",
            _ => category
        };
    }
}
