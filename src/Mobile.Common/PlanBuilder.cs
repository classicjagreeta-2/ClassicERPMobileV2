namespace Mobile.Common;

public record PlanEntry(int seq, string key, string description, string type, string method, string path);

// The list the phone follows: which masters to read, in which order, and the call for each. API 2 builds it from the client's own
// MOBILE_MASTER_CONFIG table and returns it from GET /mobile/v1/plan.
public static class PlanBuilder
{
    public static string PathOf(MasterItem i) => i.ItemKey switch
    {
        "face-consent" => "/mobile/v1/face-consent",
        "face-recognition" => "/mobile/v1/face-recognition",
        "import-eye" => "/mobile/v1/import-eye",
        _ => i.Op switch
        {
            OpType.UploadInsert => $"/mobile/v1/upload/{i.ItemKey}",
            OpType.DeviceCheck => "/mobile/v1/auth/device-check",
            _ => $"/mobile/v1/masters/{i.ItemKey}"
        }
    };

    public static List<PlanEntry> Build(IEnumerable<MasterItem> items) => items
        .Where(i => i.Op != OpType.Control).OrderBy(i => i.SeqNo)
        .Select(i => new PlanEntry(i.SeqNo, i.ItemKey, i.Description,
            i.Op switch { OpType.Read => "READ", OpType.UploadInsert => "UPLOAD", OpType.DeviceCheck => "CHECK", _ => "UPDATE" },
            i.Op is OpType.Read or OpType.DeviceCheck ? "GET" : "POST", PathOf(i)))
        .ToList();
}
