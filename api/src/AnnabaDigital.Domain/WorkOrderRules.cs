using AnnabaDigital.Domain.Entities;

namespace AnnabaDigital.Domain;

public enum WorkOrderAction { Start, Hold, Resume, Complete, Cancel }

/// <summary>TKT-006/007/014. The pure rules of work orders: the moves between states, what completing needs, and the limits of checklists, time and cost lines.</summary>
public static class WorkOrderRules
{
    public const int TitleMin = 3, TitleMax = 150, InstructionsMax = 4000, MaxTasks = 50, TaskMax = 300, NoteMax = 2000, ReasonMin = 3, ReasonMax = 500;
    public const int MaxMinutes = 24 * 60, DescriptionMax = 200;
    public const decimal QuantityMax = 100_000m, UnitCostMax = 10_000_000_000m;
    /// <summary><c>invoice</c> (R2-8): a vendor's invoice for the work.</summary>
    public static readonly string[] CostKinds = ["part", "labour", "service", "invoice", "other"];

    public static WorkOrderStatus? Next(WorkOrderStatus s, WorkOrderAction a) => (s, a) switch
    {
        (WorkOrderStatus.Open, WorkOrderAction.Start) => WorkOrderStatus.InProgress,
        (WorkOrderStatus.InProgress, WorkOrderAction.Hold) => WorkOrderStatus.OnHold,
        (WorkOrderStatus.OnHold, WorkOrderAction.Resume) => WorkOrderStatus.InProgress,
        (WorkOrderStatus.InProgress, WorkOrderAction.Complete) => WorkOrderStatus.Done,
        (WorkOrderStatus.Open or WorkOrderStatus.InProgress or WorkOrderStatus.OnHold, WorkOrderAction.Cancel) => WorkOrderStatus.Cancelled,
        _ => null,
    };

    public static bool IsFinished(WorkOrderStatus s) => s is WorkOrderStatus.Done or WorkOrderStatus.Cancelled;
    /// <summary>Still holding its ticket open: required and neither done nor cancelled.</summary>
    public static bool Blocks(WorkOrder w) => w.Required && !IsFinished(w.Status);

    public static string StatusName(WorkOrderStatus s) => s switch { WorkOrderStatus.InProgress => "in_progress", WorkOrderStatus.OnHold => "on_hold", _ => s.ToString().ToLowerInvariant() };

    /// <summary>What still stands in the way of completing: required checklist lines not ticked, and a missing "after" photo when one is required.</summary>
    public static List<string> MissingForCompletion(WorkOrder w, IEnumerable<WorkOrderTask> tasks, int afterPhotos)
    {
        var missing = new List<string>();
        var open = tasks.Where(t => t.Required && t.DoneAt is null).ToList();
        if (open.Count > 0) missing.Add(open.Count == 1 ? $"The checklist line “{open[0].Text}” is not done." : $"{open.Count} required checklist lines are not done.");
        if (w.RequireAfterPhoto && afterPhotos == 0) missing.Add("Add a photo of the finished work (an “after” photo).");
        return missing;
    }
}
