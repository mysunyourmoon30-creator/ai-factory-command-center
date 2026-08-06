namespace AI.Factory.Core.Alerts;

/// <summary>
/// Re-derives all 5 alert types from current data and upserts/resolves rows against the locked
/// unique filtered index (AlertType + EntityName + EntityId WHERE IsActive = 1), so calling this
/// repeatedly - e.g. on every screen refresh - never creates a duplicate active alert.
/// </summary>
public interface IAlertEvaluationService
{
    Task EvaluateAsync(CancellationToken cancellationToken = default);
}
