using System;
using System.Collections.Generic;
using System.Linq;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Study;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Storage;

public sealed class InMemoryStorage : IStorage
{
    private readonly object _lock = new();

    private sealed class StudyRecord
    {
        public required int StudyId { get; init; }
        public required StudyDirection Direction { get; init; }
        public required string Name { get; init; }
        public readonly List<int> TrialIds = new();
    }

    private readonly Dictionary<int, StudyRecord> _studies = new();
    private readonly Dictionary<int, FrozenTrial> _trials = new();
    private int _nextStudyId;
    private int _nextTrialId;

    public int CreateNewStudy(StudyDirection direction, string studyName)
    {
        lock (_lock)
        {
            int id = _nextStudyId++;
            _studies[id] = new StudyRecord { StudyId = id, Direction = direction, Name = studyName };
            return id;
        }
    }

    public StudyDirection GetStudyDirection(int studyId)
    {
        lock (_lock)
            return GetStudy(studyId).Direction;
    }

    public int CreateNewTrial(int studyId)
    {
        lock (_lock)
        {
            var study = GetStudy(studyId);
            int trialId = _nextTrialId++;
            int number = study.TrialIds.Count;
            var trial = new FrozenTrial(trialId, number, TrialState.Running)
            {
                DateTimeStart = DateTime.UtcNow,
            };
            _trials[trialId] = trial;
            study.TrialIds.Add(trialId);
            return trialId;
        }
    }

    public void SetTrialParam(int trialId, string name, double internalValue, Distribution distribution)
    {
        lock (_lock)
        {
            var trial = GetTrialInternal(trialId);
            EnsureUpdatable(trial);
            trial.InternalParams[name] = internalValue;
            trial.Params[name] = distribution.ToExternalRepr(internalValue);
            trial.Distributions[name] = distribution;
        }
    }

    public bool SetTrialStateValue(int trialId, TrialState state, double? value)
    {
        lock (_lock)
        {
            var trial = GetTrialInternal(trialId);
            if (trial.State.IsFinished())
                return false;
            trial.State = state;
            trial.Value = value;
            if (state.IsFinished())
                trial.DateTimeComplete = DateTime.UtcNow;
            return true;
        }
    }

    public void SetTrialUserAttr(int trialId, string key, object value)
    {
        lock (_lock)
        {
            var trial = GetTrialInternal(trialId);
            EnsureUpdatable(trial);
            trial.UserAttrs[key] = value;
        }
    }

    public FrozenTrial GetTrial(int trialId)
    {
        lock (_lock)
            return GetTrialInternal(trialId).Clone();
    }

    public IReadOnlyList<FrozenTrial> GetAllTrials(int studyId, IReadOnlyCollection<TrialState>? states = null)
    {
        lock (_lock)
        {
            var study = GetStudy(studyId);
            var result = new List<FrozenTrial>(study.TrialIds.Count);
            foreach (var id in study.TrialIds)
            {
                var t = _trials[id];
                if (states == null || states.Contains(t.State))
                    result.Add(t.Clone());
            }
            return result;
        }
    }

    public int GetNTrials(int studyId)
    {
        lock (_lock)
            return GetStudy(studyId).TrialIds.Count;
    }

    public FrozenTrial? GetBestTrial(int studyId)
    {
        lock (_lock)
        {
            var study = GetStudy(studyId);
            FrozenTrial? best = null;
            foreach (var id in study.TrialIds)
            {
                var t = _trials[id];
                if (t.State != TrialState.Complete || t.Value is not { } v)
                    continue;
                if (best == null)
                {
                    best = t;
                    continue;
                }
                double bv = best.Value!.Value;
                bool better = study.Direction == StudyDirection.Maximize ? v > bv : v < bv;
                if (better)
                    best = t;
            }
            return best?.Clone();
        }
    }

    private StudyRecord GetStudy(int studyId)
    {
        if (!_studies.TryGetValue(studyId, out var s))
            throw new KeyNotFoundException($"No study with id {studyId}.");
        return s;
    }

    private FrozenTrial GetTrialInternal(int trialId)
    {
        if (!_trials.TryGetValue(trialId, out var t))
            throw new KeyNotFoundException($"No trial with id {trialId}.");
        return t;
    }

    private static void EnsureUpdatable(FrozenTrial trial)
    {
        if (trial.State.IsFinished())
            throw new InvalidOperationException($"Trial {trial.TrialId} is finished and cannot be modified.");
    }
}
