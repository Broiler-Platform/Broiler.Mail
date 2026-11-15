// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   8
// Annotated:        8/8
// Exempt:           0
// Human-reviewed:   0/8
// IP risk:          None
// Security risk:    Medium
// Criteria:         1/0
// Resource impact:  2/10 max
// Unverified:       8
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.UI;

namespace Broiler.Mail.Application.Views;

/// <summary>
/// Attaches an asynchronous UI event handler whose code after each <c>await</c> resumes on the sender's UI
/// dispatcher. No host installs a <see cref="SynchronizationContext"/>, and a view model's task completes on a
/// pool thread as soon as it has posted its commit. Without this, a handler resumed there, changed the element
/// tree while the UI thread laid it out, and ran before the commit it awaited had been applied.
/// </summary>
// Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=2; Fingerprint=C22F4A
// Hairs-Human:        PENDING
internal static class UiAsync
{
    /// <summary>
    /// Wraps <paramref name="handler"/> so that its continuations run on the sender's session dispatcher. The
    /// argument type is written at each call, because C# cannot infer it from a lambda with discarded parameters.
    /// </summary>
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=913742
    // Hairs-Human:        PENDING
    public static EventHandler<T> Handler<T>(EventHandler<T> handler) => (sender, e) => Raise(sender, () => handler(sender, e));

    // An async handler captures the current context when it is invoked and at each await, so the context only
    // has to be current while the handler runs synchronously, here and in each continuation the context posts.
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=2; Fingerprint=D2E7D9
    // Hairs-Human:        PENDING
    private static void Raise(object? sender, Action raise)
    {
        if ((sender as UiElement)?.Session?.Dispatcher is not { } dispatcher)
        {
            raise();
            return;
        }

        RunIn(new DispatcherContext(dispatcher), raise);
    }

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=1E23A0
    // Hairs-Human:        PENDING
    private static void RunIn(SynchronizationContext context, Action action)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try { action(); }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    /// <summary>
    /// Queues continuations, and an exception escaping an async void handler, on the UI dispatcher in order,
    /// after anything the awaited operation has already posted there.
    /// </summary>
    // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=2; Fingerprint=19319A
    // Hairs-Falsified-If: a handler's code after an await runs on a thread other than the dispatcher's, or before a commit the awaited view model posted
    // Hairs-Human:        PENDING
    private sealed class DispatcherContext(IUiDispatcher dispatcher) : SynchronizationContext
    {
        // Hairs-AI:           Origin=AI; IP=None; Security=Medium; Resources=1; Fingerprint=E91A96
        // Hairs-Human:        PENDING
        public override void Post(SendOrPostCallback callback, object? state)
        {
            try { dispatcher.Post(() => RunIn(this, () => callback(state))); }
            catch (ObjectDisposedException) { /* The window has closed; nothing is left for the handler to update. */ }
        }

        // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=0; Fingerprint=BDA01E
        // Hairs-Human:        PENDING
        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (!dispatcher.CheckAccess())
                throw new NotSupportedException("A UI handler cannot wait for the UI thread from another thread.");
            callback(state);
        }

        // Hairs-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=DAF7D3
        // Hairs-Human:        PENDING
        public override SynchronizationContext CreateCopy() => new DispatcherContext(dispatcher);
    }
}
