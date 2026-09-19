using System.Globalization;

namespace Ashenwake.Core.Progression;

public enum ProgressionBuildAction { AllocatePassive, Respec, SelectMutation }
public sealed record ProgressionBuildRequest(ProgressionBuildAction Action, string Id = "", string Value = "");
public sealed record ProgressionBuildPreview(bool Success, string Reason, ProgressionSnapshot Before, ProgressionSnapshot After,
    ProgressionView BeforeView, ProgressionView AfterView);

public sealed partial class ProgressionSession
{
    /// <summary>Projects an existing build transaction on detached state without granting points or bypassing mastery.</summary>
    public ProgressionBuildPreview PreviewBuild(ProgressionBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var before = Capture();
        var projection = Restore(content, before);
        var beforeView = projection.View;
        string operationId = "build.preview";
        for (int suffix = 0; before.Character.OperationReceipts.ContainsKey(operationId); suffix++)
            operationId = "build.preview." + suffix.ToString(CultureInfo.InvariantCulture);
        var result = request.Action switch
        {
            ProgressionBuildAction.AllocatePassive => projection.AllocatePassive(operationId, request.Id),
            ProgressionBuildAction.Respec => projection.Respec(operationId),
            ProgressionBuildAction.SelectMutation => projection.SelectMutation(operationId, request.Id, request.Value),
            _ => new ProgressionResult(false, "Unknown build action.", [])
        };
        // Each successful hypothetical receipt belongs only to this projection. Failed transactions already
        // roll back through the same Change path used by live progression.
        return new(result.Success, result.Reason, before, projection.Capture(), beforeView, projection.View);
    }
}
