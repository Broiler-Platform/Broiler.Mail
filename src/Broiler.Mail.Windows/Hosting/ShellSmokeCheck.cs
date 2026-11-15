// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0
//
// HAIRS Code Assurance
// --------------------
// Relevant units:   7
// Annotated:        7/7
// Exempt:           1
// Human-reviewed:   0/7
// IP risk:          Low
// Security risk:    Low
// Criteria:         2/0
// Resource impact:  3/10 max
// Unverified:       7
//
// GENERATED - DO NOT EDIT MANUALLY

using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Mail.Application.Views;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Mail.Windows.Hosting;

/// <summary>Checks composition and layout without opening a native window or touching accounts.</summary>
// Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=A36D7A
// Hairs-Falsified-If: the smoke run opens a native window or reads account data or credentials
// Hairs-Human:        PENDING
internal static class ShellSmokeCheck
{
    // Hairs-AI:           Origin=AI; IP=Low; Security=Low; Resources=3; Fingerprint=30B56C
    // Hairs-Falsified-If: a shell missing one of the inbox, account, settings or compose views lets Run return without an exception
    // Hairs-Human:        PENDING
    public static void Run(MailShellView shell)
    {
        using var session = new StandardUiSessionBuilder().Build(new HeadlessHost());
        shell.Attach(session);
        foreach (string tabId in new[] { "inbox", "account", "settings", "compose", "outbox", "originals", "unified" })
        {
            if (!shell.ShowView(tabId))
                throw new InvalidOperationException($"Missing shell view: {tabId}");
            _ = session.RenderFrame();
        }
    }

    // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=328396
    // Hairs-Human:        PENDING
    private sealed class HeadlessHost : IUiHost
    {
        // Hairs-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=2FCBE2
        // Hairs-Human:        PENDING
        public BSize ViewportSize => new(1100, 720);
        public double Scale => 1;
        // Hairs-AI:           Origin=AI; IP=None; Security=Low; Resources=1; Fingerprint=516BFD
        // Hairs-Human:        PENDING
        public BRenderList CreateRenderList(int capacity = 0) => new(capacity);
        // Hairs-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=98E497
        // Hairs-Human:        PENDING
        public void Invalidate(UiInvalidation invalidation) { }
        // Hairs-AI:           Origin=AI; IP=None; Security=None; Resources=0; Fingerprint=E9FEA9
        // Hairs-Human:        PENDING
        public void Present(BRenderList renderList) { }
    }
}
