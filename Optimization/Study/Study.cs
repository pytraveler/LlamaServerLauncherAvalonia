using System;
using System.Collections.Generic;
using System.Threading;
using LlamaServerLauncher.Optimization.Pruners;
using LlamaServerLauncher.Optimization.Samplers;
using LlamaServerLauncher.Optimization.Storage;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Study;

public sealed class Study
{
    public IStorage Storage { get; }
    public ISampler Sampler { get; }
    public IPruner Pruner { get; }
    public StudyDirection Direction { get; }
    public int StudyId { get; }

    private Study(IStorage storage, ISampler sampler, IPruner pruner, StudyDirection direction, int studyId)
    {
        Storage = storage;
        Sampler = sampler;
        Pruner = pruner;
        Direction = direction;
        StudyId = studyId;
    }

    public static Study Create(
        IStorage storage,
        ISampler sampler,
        StudyDirection direction = StudyDirection.Minimize,
        IPruner? pruner = null,
        string studyName = "study")
    {
        int id = storage.CreateNewStudy(direction, studyName);
        return new Study(storage, sampler, pruner ?? new NopPruner(), direction, id);
    }

    public void Optimize(Func<Trial.Trial, double> objective, int nTrials, CancellationToken ct = default)
    {
        for (int i = 0; i < nTrials; i++)
        {
            ct.ThrowIfCancellationRequested();

            var trial = Ask();
            double? value = null;
            TrialState state;
            try
            {
                value = objective(trial);
                state = TrialState.Complete;
            }
            catch (OperationCanceledException)
            {
                Storage.SetTrialStateValue(trial.TrialId, TrialState.Fail, null);
                throw;
            }
            catch (TrialPruned)
            {
                state = TrialState.Pruned;
            }
            catch (Exception)
            {
                state = TrialState.Fail;
            }

            Tell(trial, state == TrialState.Complete ? value : null, state);
        }
    }

    public async System.Threading.Tasks.Task OptimizeAsync(
        Func<Trial.Trial, System.Threading.Tasks.Task<double>> objective,
        int nTrials,
        CancellationToken ct = default)
    {
        for (int i = 0; i < nTrials; i++)
        {
            ct.ThrowIfCancellationRequested();

            var trial = Ask();
            double? value = null;
            TrialState state;
            try
            {
                value = await objective(trial);
                state = TrialState.Complete;
            }
            catch (OperationCanceledException)
            {
                Storage.SetTrialStateValue(trial.TrialId, TrialState.Fail, null);
                throw;
            }
            catch (TrialPruned)
            {
                state = TrialState.Pruned;
            }
            catch (Exception)
            {
                state = TrialState.Fail;
            }

            Tell(trial, state == TrialState.Complete ? value : null, state);
        }
    }

    public Trial.Trial Ask()
    {
        int trialId = Storage.CreateNewTrial(StudyId);
        var sampler = Sampler;
        sampler.BeforeTrial(this, Storage.GetTrial(trialId));
        return new Trial.Trial(this, trialId);
    }

    public void Tell(Trial.Trial trial, double? value, TrialState state = TrialState.Complete)
    {
        Storage.SetTrialStateValue(trial.TrialId, state, state == TrialState.Complete ? value : null);
        Sampler.AfterTrial(this, Storage.GetTrial(trial.TrialId), state, value);
    }

    public IReadOnlyList<FrozenTrial> Trials => Storage.GetAllTrials(StudyId);

    public IReadOnlyList<FrozenTrial> GetTrials(IReadOnlyCollection<TrialState>? states) =>
        Storage.GetAllTrials(StudyId, states);

    public FrozenTrial? BestTrial => Storage.GetBestTrial(StudyId);

    public double BestValue =>
        BestTrial?.Value ?? throw new InvalidOperationException("No completed trials in study.");

    public IReadOnlyDictionary<string, object> BestParams =>
        BestTrial?.Params ?? throw new InvalidOperationException("No completed trials in study.");
}
