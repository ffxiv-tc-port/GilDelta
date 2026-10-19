using System;

namespace GilDelta.Events;

/// <summary>
/// The retention policy, kept apart from the plugin plumbing so the one rule that
/// decides whether records get deleted is unit-testable on its own.
/// </summary>
public static class Retention
{
    /// <summary>
    /// Cutoff for a retention window, matching the setting's own label
    /// ("Retention (days, 0 = forever)").
    /// </summary>
    /// <returns>
    /// null when nothing may ever be deleted - which is what 0 means, and therefore
    /// what every existing user gets, because 0 is the default. A negative value is
    /// treated the same way rather than being clamped into a deletion.
    /// </returns>
    public static DateTimeOffset? CutoffFor(int retentionDays, DateTimeOffset now) =>
        retentionDays <= 0 ? null : now.AddDays(-retentionDays);
}
