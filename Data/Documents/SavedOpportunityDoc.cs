namespace RadarV2.Data.Documents;

public class SavedOpportunityDoc
{
    public string Id             { get; set; } = Guid.NewGuid().ToString();
    public string UserId         { get; set; } = string.Empty;
    public string OpportunityId  { get; set; } = string.Empty;
    public DateTime SavedAt      { get; set; } = DateTime.UtcNow;
}
