using System.Collections.Generic;
using LlamaServerLauncher.Optimization.Distributions;
using LlamaServerLauncher.Optimization.Study;
using LlamaServerLauncher.Optimization.Trial;

namespace LlamaServerLauncher.Optimization.Storage;

public interface IStorage
{
    int CreateNewStudy(StudyDirection direction, string studyName);

    StudyDirection GetStudyDirection(int studyId);

    int CreateNewTrial(int studyId);

    void SetTrialParam(int trialId, string name, double internalValue, Distribution distribution);

    bool SetTrialStateValue(int trialId, TrialState state, double? value);

    void SetTrialUserAttr(int trialId, string key, object value);

    FrozenTrial GetTrial(int trialId);

    IReadOnlyList<FrozenTrial> GetAllTrials(int studyId, IReadOnlyCollection<TrialState>? states = null);

    int GetNTrials(int studyId);

    FrozenTrial? GetBestTrial(int studyId);
}
