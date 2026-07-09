using System.Runtime.InteropServices;
using WinRtContent = Windows.UI.Shell.Tasks.AppTaskContent;
using WinRtInfo = Windows.UI.Shell.Tasks.AppTaskInfo;
using WinRtState = Windows.UI.Shell.Tasks.AppTaskState;

namespace Atv.Store;

/// <summary>
/// The real <see cref="IAppTaskStore"/> implementation, and the SOLE file in
/// this codebase that imports <c>Windows.UI.Shell.Tasks</c> (plan/README.md
/// standing invariant #7; enforced by
/// <c>tests/Atv.LogicTests/Architecture/SeamPurityTests.cs</c>, which greps
/// the source tree). Every other type -- CLI logic, validators, the sidecar,
/// the watchdog -- talks only to <see cref="IAppTaskStore"/>, so a
/// platform/contract change (this is an <c>[Experimental]</c> namespace,
/// INFRA-13) has its blast radius localized to this one file.
///
/// Stateless: every member is a thin, self-contained translation to/from the
/// platform's static <c>AppTaskInfo</c> surface. Holds no
/// <see cref="System.Threading.Mutex"/> and knows nothing about cross-process
/// locking -- see <see cref="IAppTaskStore"/>'s remarks on where that lives.
///
/// Real-API coverage (adapter fidelity, ≥1 round-trip per member here against
/// the actual platform) is phase 03's job, not this one; this file is
/// exercised only by hand/POC-smoke until then (out of scope here per
/// plan/phase-02-core-seam.md).
/// </summary>
public sealed class AppTaskStore : IAppTaskStore
{
    public bool IsSupported()
    {
        try
        {
            return WinRtInfo.IsSupported();
        }
        catch (COMException)
        {
            // CLASS_E_CLASSNOTAVAILABLE on builds where AppTaskInfo's
            // activation registration isn't rolled out yet (INFRA-13) --
            // runtime capability detection, never version pinning.
            return false;
        }
    }

    public IReadOnlyList<AppTaskView> FindAll()
        => (WinRtInfo.FindAll() ?? []).Select(ToView).ToArray();

    public AppTaskView? Find(string id)
        => FindNative(id) is { } native ? ToView(native) : null;

    public AppTaskView Create(string title, string subtitle, Uri deepLink, Uri iconUri, AppTaskContentDto content)
    {
        var native = WinRtInfo.Create(title, subtitle, deepLink, iconUri, ToNativeContent(content));
        return ToView(native);
    }

    public bool Update(string id, AppTaskState state, AppTaskContentDto content)
    {
        if (FindNative(id) is not { } native) return false;
        native.Update(ToNativeState(state), ToNativeContent(content));
        return true;
    }

    public bool UpdateState(string id, AppTaskState state)
    {
        if (FindNative(id) is not { } native) return false;
        native.UpdateState(ToNativeState(state));
        return true;
    }

    public bool UpdateTitles(string id, string title, string subtitle)
    {
        if (FindNative(id) is not { } native) return false;
        native.UpdateTitles(title, subtitle);
        return true;
    }

    public bool UpdateDeepLink(string id, Uri deepLink)
    {
        if (FindNative(id) is not { } native) return false;
        native.UpdateDeepLink(deepLink);
        return true;
    }

    public bool Remove(string id)
    {
        if (FindNative(id) is not { } native) return false;
        native.Remove();
        return true;
    }

    // The platform has no by-Id lookup primitive; every Id-addressed operation
    // above filters FindAll() -- the same pattern AppTaskInfo.md's own
    // "typical lifecycle" recommends for recovering tasks at startup.
    private static WinRtInfo? FindNative(string id)
        => (WinRtInfo.FindAll() ?? []).FirstOrDefault(t => t.Id == id);

    private static AppTaskView ToView(WinRtInfo native) => new(
        native.Id,
        native.Title,
        native.Subtitle,
        ToDtoState(native.State),
        native.StartTime,
        native.EndTime,
        native.DeepLink,
        native.IconUri,
        native.HiddenByUser,
        native.GetCompletedSteps() ?? [],
        native.GetExecutingStep() ?? "");

    private static WinRtContent ToNativeContent(AppTaskContentDto content)
    {
        var native = content switch
        {
            AppTaskContentDto.SequenceOfSteps s => WinRtContent.CreateSequenceOfSteps([.. s.CompletedSteps], s.ExecutingStep),
            AppTaskContentDto.TextSummaryResult t => WinRtContent.CreateTextSummaryResult(t.Text),
            _ => throw new NotSupportedException($"Unknown {nameof(AppTaskContentDto)} shape: {content.GetType()}"),
        };
        if (content.Question is { } question)
            native.SetQuestion(question);
        return native;
    }

    private static WinRtState ToNativeState(AppTaskState state) => state switch
    {
        AppTaskState.Running => WinRtState.Running,
        AppTaskState.Completed => WinRtState.Completed,
        AppTaskState.NeedsAttention => WinRtState.NeedsAttention,
        AppTaskState.Paused => WinRtState.Paused,
        AppTaskState.Error => WinRtState.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };

    private static AppTaskState ToDtoState(WinRtState state) => state switch
    {
        WinRtState.Running => AppTaskState.Running,
        WinRtState.Completed => AppTaskState.Completed,
        WinRtState.NeedsAttention => AppTaskState.NeedsAttention,
        WinRtState.Paused => AppTaskState.Paused,
        WinRtState.Error => AppTaskState.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
