using System.Collections.Generic;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Samplers;

public interface ISampler
{
    double SampleIndependent(
        Study.Study study,
        FrozenTrial trial,
        string paramName,
        Distribution distribution);

    void BeforeTrial(Study.Study study, FrozenTrial trial) { }

    void AfterTrial(Study.Study study, FrozenTrial trial, TrialState state, double? value) { }

    void Reseed(int seed) { }
}
