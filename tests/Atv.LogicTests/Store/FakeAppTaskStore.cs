using Codevoid.AgentTaskVoid.Store;

namespace Codevoid.AgentTaskVoid.LogicTests.Store;

/// <summary>
/// In-memory <see cref="IAppTaskStore"/> test double. Test-only -- lives in
/// this test project, not <c>src/Atv</c>, and ships nowhere.
///
/// FIDELITY PROMISES (INFRA-15): the single source of truth for this list is
/// <c>docs/testing/fake-fidelity-promises.md</c>; this class implements
/// exactly those four and nothing else that models real-platform behavior. Do
/// not add a fifth without updating that doc first.
///
///  1. Non-atomic whole-store clobber / last-writer-wins, via
///     <see cref="InterleaveHook"/> -- see <see cref="MutateWholeStore"/>.
///  2. <see cref="AppTaskView.HiddenByUser"/> surfacing -- via <see cref="SetHiddenByUser"/>.
///  3. Out-of-band drift -- <see cref="SimulateVanish"/> (delete behind the
///     logic's back) and <see cref="SeedEntrylessTask"/> (a task the logic
///     never created); unknown/vanished-Id ops return a clean not-found on
///     every member (no separate hook needed -- it's the default behavior).
///  4. Opaque Id minting -- see <see cref="MintId"/>.
///
/// Explicitly NOT modeled (INFRA-15's "must not" list): the state x content
/// crash matrix, Shell rendering/grouping, the file-watcher, timing/latency,
/// exact COMException codes, and no generic error-injection mode. No
/// convenience content merge/append either -- every write here is
/// whole-content replacement, exactly like the real platform (ERGO-8).
/// </summary>
public sealed class FakeAppTaskStore : IAppTaskStore
{
    private List<AppTaskView> _tasks = [];
    private int _nextId;

    /// <summary>
    /// Always <see langword="true"/> by default -- the fake models a machine
    /// where the API is present. Settable for future doctor/diagnostics-verb
    /// tests that need to simulate an unsupported machine.
    /// </summary>
    public bool Supported { get; set; } = true;

    /// <summary>
    /// FIDELITY PROMISE 1 (INFRA-15): fires exactly once per mutating call
    /// (<see cref="Create"/>/<see cref="Update"/>/<see cref="UpdateState"/>/
    /// <see cref="UpdateTitles"/>/<see cref="UpdateDeepLink"/>/<see cref="Remove"/>),
    /// after that call's whole-store snapshot is taken but before it commits
    /// its own new snapshot -- modeling "another writer committed between your
    /// read and your write" against the real, non-atomic <c>tasks.json</c>
    /// (README.md, "Concurrency: writes are not serialized across
    /// processes"). A test sets this to perform a second store mutation
    /// (typically clearing the hook first, so it doesn't recurse); when the
    /// outer call then commits, it blindly overwrites with its own
    /// stale-snapshot-derived state, silently losing whatever the hook's
    /// mutation did -- deterministic last-writer-wins loss, matching the
    /// empirical 4x100 -&gt; 37/400 result documented in
    /// docs/windows-ui-shell-tasks/README.md. <see langword="null"/> (the
    /// default) means no interleaving; calls behave atomically.
    /// </summary>
    public Action? InterleaveHook { get; set; }

    public bool IsSupported() => Supported;

    public IReadOnlyList<AppTaskView> FindAll() => _tasks.ToArray();

    public AppTaskView? Find(string id) => _tasks.FirstOrDefault(t => t.Id == id);

    public AppTaskView Create(string title, string subtitle, Uri deepLink, Uri iconUri, AppTaskContentDto content)
    {
        var (steps, executingStep) = StepsOf(content);
        var view = new AppTaskView(
            MintId(), title, subtitle, AppTaskState.Running,
            DateTimeOffset.Now, null, deepLink, iconUri, HiddenByUser: false,
            steps, executingStep);

        return MutateWholeStore(snapshot =>
        {
            snapshot.Add(view);
            return view;
        });
    }

    public bool Update(string id, AppTaskState state, AppTaskContentDto content)
        => TryReplace(id, existing =>
        {
            var (steps, executingStep) = StepsOf(content);
            return existing with
            {
                State = state,
                EndTime = IsEndingState(state) ? DateTimeOffset.Now : existing.EndTime,
                CompletedSteps = steps,
                ExecutingStep = executingStep,
            };
        });

    public bool UpdateState(string id, AppTaskState state)
        => TryReplace(id, existing => existing with
        {
            State = state,
            EndTime = IsEndingState(state) ? DateTimeOffset.Now : existing.EndTime,
        });

    public bool UpdateTitles(string id, string title, string subtitle)
        => TryReplace(id, existing => existing with { Title = title, Subtitle = subtitle });

    public bool UpdateDeepLink(string id, Uri deepLink)
        => TryReplace(id, existing => existing with { DeepLink = deepLink });

    public bool Remove(string id)
        => MutateWholeStore(snapshot =>
        {
            int index = snapshot.FindIndex(t => t.Id == id);
            if (index < 0) return false;
            snapshot.RemoveAt(index);
            return true;
        });

    /// <summary>
    /// FIDELITY PROMISE 2 (INFRA-15): models the Shell's per-card dismiss (X)
    /// gesture setting <c>AppTaskInfo.HiddenByUser</c> -- an out-of-band
    /// mutation the app didn't request, surfaced through
    /// <see cref="FindAll"/>/<see cref="Find"/> exactly like the real
    /// platform (README.md, "Taskbar grouping mechanics"). No-op if
    /// <paramref name="id"/> is unknown.
    /// </summary>
    public void SetHiddenByUser(string id, bool hidden)
        => TryReplace(id, existing => existing with { HiddenByUser = hidden });

    /// <summary>
    /// FIDELITY PROMISE 3 (INFRA-15), disappearance half: deletes a task
    /// directly, bypassing <see cref="Remove"/> and its whole-store-clobber
    /// hook -- models the task vanishing behind the logic's back (e.g. a
    /// concurrent writer's clobber, or any other out-of-band loss). No-op if
    /// <paramref name="id"/> is unknown.
    /// </summary>
    public void SimulateVanish(string id) => _tasks = [.. _tasks.Where(t => t.Id != id)];

    /// <summary>
    /// FIDELITY PROMISE 3 (INFRA-15), entryless half: inserts a task directly,
    /// as if the platform already knew about it before this process's sidecar
    /// (phase 04) ever recorded a handle -&gt; Id mapping for it -- the "API
    /// knows it, sidecar doesn't" entryless state ERGO-21's reconciliation has
    /// to handle. Still mints an opaque Id the same way <see cref="Create"/>
    /// does (fidelity promise 4 holds for seeded entries too), and goes
    /// through the same whole-store commit path as every other mutation.
    /// </summary>
    public AppTaskView SeedEntrylessTask(
        string title,
        string subtitle,
        AppTaskState state = AppTaskState.Running,
        AppTaskContentDto? content = null,
        Uri? deepLink = null,
        Uri? iconUri = null)
    {
        var (steps, executingStep) = StepsOf(content ?? new AppTaskContentDto.SequenceOfSteps([], ""));
        var view = new AppTaskView(
            MintId(), title, subtitle, state,
            DateTimeOffset.Now, IsEndingState(state) ? DateTimeOffset.Now : null,
            deepLink ?? new Uri("https://example.invalid/seeded"),
            iconUri ?? new Uri("ms-appx:///Assets/Square44x44Logo.png"),
            HiddenByUser: false, steps, executingStep);

        return MutateWholeStore(snapshot =>
        {
            snapshot.Add(view);
            return view;
        });
    }

    private bool TryReplace(string id, Func<AppTaskView, AppTaskView> mutate)
        => MutateWholeStore(snapshot =>
        {
            int index = snapshot.FindIndex(t => t.Id == id);
            if (index < 0) return false;
            snapshot[index] = mutate(snapshot[index]);
            return true;
        });

    /// <summary>
    /// The one chokepoint every mutating member goes through -- see
    /// <see cref="InterleaveHook"/> for what this models and why.
    /// </summary>
    private TResult MutateWholeStore<TResult>(Func<List<AppTaskView>, TResult> mutation)
    {
        var snapshot = new List<AppTaskView>(_tasks); // "read" the whole store
        InterleaveHook?.Invoke(); // another writer may commit into _tasks here
        var result = mutation(snapshot); // compute the new whole store from the now-stale snapshot
        _tasks = snapshot; // "write" -- blind whole-store replace, clobbering anything the hook committed
        return result;
    }

    /// <summary>
    /// FIDELITY PROMISE 4 (INFRA-15): an opaque id sharing no format with the
    /// real platform's, so no logic test can accidentally pass by assuming
    /// one.
    /// </summary>
    private string MintId() => $"fake-task-{++_nextId:x8}-{Guid.NewGuid():N}";

    private static bool IsEndingState(AppTaskState state) => state is AppTaskState.Completed or AppTaskState.Error;

    private static (IReadOnlyList<string> Steps, string ExecutingStep) StepsOf(AppTaskContentDto content) => content switch
    {
        AppTaskContentDto.SequenceOfSteps s => (s.CompletedSteps, s.ExecutingStep),
        _ => ([], ""),
    };
}
