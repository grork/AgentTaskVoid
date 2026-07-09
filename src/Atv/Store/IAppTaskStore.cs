namespace Atv.Store;

/// <summary>
/// Repository seam between CLI logic and the platform's durable task store
/// (INFRA-8, "The seam between CLI logic and the WinRT API for unit testing").
/// Id-addressed CRUD keyed by the platform's own <c>AppTaskInfo.Id</c> --
/// DTO-in/DTO-out, so no projected WinRT type ever crosses this interface.
///
/// <see cref="AppTaskStore"/> is the sole real implementation, and the sole
/// importer of <c>Windows.UI.Shell.Tasks</c> anywhere in this codebase
/// (plan/README.md standing invariant #7 -- enforced by
/// <c>tests/Atv.LogicTests/Architecture/SeamPurityTests.cs</c>).
/// <c>FakeAppTaskStore</c> (test project only) is the in-memory test double
/// with the INFRA-15 fidelity promises documented in
/// <c>docs/testing/fake-fidelity-promises.md</c>.
///
/// Deliberately thin: this interface exposes exactly what <c>AppTaskInfo</c>'s
/// own static/instance surface exposes, nothing more. Everything else --
/// caller-handle-to-Id mapping (the sidecar, phase 04), the ERGO-10
/// safe-combination validator, the ERGO-8 "advance" content model, and all
/// write orchestration -- lives ABOVE this seam as plain, fake-testable code.
///
/// This interface and its implementations know nothing about a
/// <see cref="System.Threading.Mutex"/>. The real platform does not serialize
/// concurrent writers (INFRA-5); the composition root is responsible for
/// acquiring the INFRA-6 cross-process write mutex around a whole
/// read-modify-write sequence of calls into this interface (wiring lands in
/// phase 04's <c>WriteGate</c>) -- nothing about this seam's shape needs to
/// change for that, since every member here is a single, self-contained
/// operation.
/// </summary>
public interface IAppTaskStore
{
    /// <summary>
    /// Mirrors <c>AppTaskInfo.IsSupported()</c>, already wrapped for the
    /// <c>CLASS_E_CLASSNOTAVAILABLE</c> <see cref="System.Runtime.InteropServices.COMException"/>
    /// some Windows 11 builds throw even with valid package identity
    /// (INFRA-13). Callers should check this before relying on any other
    /// member.
    /// </summary>
    bool IsSupported();

    /// <summary>Mirrors <c>AppTaskInfo.FindAll()</c>: every non-removed task, including hidden ones.</summary>
    IReadOnlyList<AppTaskView> FindAll();

    /// <summary>
    /// One task by Id, or <see langword="null"/> if unknown or no longer
    /// present. The real platform has no by-Id lookup primitive; the real
    /// adapter filters <c>FindAll()</c>. Never throws for an unknown/vanished
    /// id -- a clean not-found (INFRA-15 fidelity promise 3).
    /// </summary>
    AppTaskView? Find(string id);

    /// <summary>
    /// Mirrors <c>AppTaskInfo.Create(title, subtitle, deepLink, iconUri, content)</c>
    /// -- note there is no state parameter; a freshly created task always
    /// starts in <see cref="AppTaskState.Running"/>, matching the real
    /// signature. The returned view's <see cref="AppTaskView.Id"/> is minted
    /// by the platform (INFRA-15 fidelity promise 4) -- callers must treat it
    /// as opaque and never assume a format.
    /// </summary>
    AppTaskView Create(string title, string subtitle, Uri deepLink, Uri iconUri, AppTaskContentDto content);

    /// <summary>
    /// Mirrors <c>AppTaskInfo.Update(state, content)</c> on the task with the
    /// given Id -- a whole-content replacement, never a merge (ERGO-8).
    /// Returns <see langword="false"/> for an unknown/vanished id -- a clean
    /// not-found, never a throw (INFRA-15 fidelity promise 3).
    /// </summary>
    bool Update(string id, AppTaskState state, AppTaskContentDto content);

    /// <summary>Mirrors <c>AppTaskInfo.UpdateState(state)</c>. Same not-found contract as <see cref="Update"/>.</summary>
    bool UpdateState(string id, AppTaskState state);

    /// <summary>Mirrors <c>AppTaskInfo.UpdateTitles(title, subtitle)</c>. Same not-found contract as <see cref="Update"/>.</summary>
    bool UpdateTitles(string id, string title, string subtitle);

    /// <summary>Mirrors <c>AppTaskInfo.UpdateDeepLink(deepLink)</c>. Same not-found contract as <see cref="Update"/>.</summary>
    bool UpdateDeepLink(string id, Uri deepLink);

    /// <summary>
    /// Mirrors <c>AppTaskInfo.Remove()</c>. Unlike the real instance method
    /// (idempotent because it is called on an already-held instance obtained
    /// earlier), this is an Id lookup first: returns <see langword="false"/>
    /// if the id is already unknown/vanished (nothing to remove), matching the
    /// uniform not-found contract of every other member here (INFRA-15
    /// fidelity promise 3); returns <see langword="true"/> when a live task
    /// was found and removed.
    /// </summary>
    bool Remove(string id);
}
