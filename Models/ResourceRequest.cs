public class ResourceRequest
{
    public string RequestId { get; set; }
    public string ForActivity { get; set; }
    public string TrackId { get; set; }
    public string Area { get; set; }

    // Resource allocation tracking
    public List<string> AllocatedWorkerIds { get; set; } = new List<string>();
    public List<string> AllocatedLocoIds { get; set; } = new List<string>();

    public Action<ResourceRequest>? OnFulfilled { get; set; }

    public ResourceRequest(string requestId, string forActivity, string trackId, string area)
    {
        RequestId = requestId;
        ForActivity = forActivity;
        TrackId = trackId;
        Area = area;
    }
}